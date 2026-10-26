using System.Diagnostics;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;

namespace CombatSolver.Mobile;

/// <summary>
/// Mobile-owned entry point. Only built-in Godot nodes and explicit signal
/// subscriptions are used; no script subclass registration is assumed.
/// </summary>
public static class MobileSolverRuntime
{
    private sealed class Session(CombatState state, LiveCombatStamp stamp, long revision)
    {
        internal readonly CombatState State = state;
        internal readonly LiveCombatStamp Stamp = stamp;
        internal readonly long Revision = revision;
        internal readonly CancellationTokenSource Cancellation = new();
        internal readonly long ManagedBytesAtStart = GC.GetTotalMemory(false);
        internal readonly Stopwatch Clock = Stopwatch.StartNew();
        internal Task<SolverResult>? Worker;
        internal SolverProgress? Progress;
        internal string? StopReason;
        internal int BudgetMilliseconds;
        internal SolverPotionPolicy PotionPolicy;
    }

    private static Node? _host;
    private static CanvasLayer? _layer;
    private static Control? _root;
    private static PanelContainer? _panel;
    private static Button? _toggle, _search, _cancel;
    private static MobileOptionsPanel? _optionsPanel;
    private static MobileSolverOptions _options = new();
    private static Label? _text;
    private static Godot.Timer? _timer;
    private static SceneTree? _tree;
    private static Window? _window;
    private static Session? _session;
    private static CombatState? _observedCombat;
    private static LiveCombatStamp? _resultStamp;
    private static long _resultRevision;
    private static long _liveRevision;
    private static long _lastFrameTicks;
    [ThreadStatic] private static bool _capturingRoot;
    private static readonly SearchFramePressureSignal Frames = new();
    private static readonly FieldInfo PowerDynamicVars = typeof(PowerModel).GetField(
        "_dynamicVars", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
        ?? throw new MissingFieldException(typeof(PowerModel).FullName, "_dynamicVars");

    public static void GameReadyPostfix(object __instance)
    {
        if (__instance is Node host) Attach(host);
    }

    /// <summary>Called from a statically woven NGame._Ready postfix.</summary>
    public static void Attach(Node host)
    {
        if (_host == host && IsValid(_layer)) return;
        try
        {
            Detach();
            Entry.InitializeMobile();
            _options = MobileSolverOptions.Load();
            _host = host;
            _tree = host.GetTree();
            BuildUi(host);
            _tree.ProcessFrame += OnFrame;
            _window = host.GetWindow();
            _window.FocusExited += OnFocusLost;
            host.TreeExiting += Detach;
            _lastFrameTicks = Stopwatch.GetTimestamp();
            _timer = new Godot.Timer
            {
                Name = "CombatSolverMobileTimer", WaitTime = 0.2,
                OneShot = false, IgnoreTimeScale = true,
            };
            _layer!.AddChild(_timer);
            _timer.Timeout += Tick;
            _timer.Start();
        }
        catch (Exception error)
        {
            GD.PushError("Mobile solver UI setup failed: " + error);
            Detach();
        }
    }

    /// <summary>Static prefix on CombatStateTracker.NotifyCombatStateChanged.</summary>
    public static bool NotificationPrefix(string caller)
    {
        if (SimulationNotificationIsolation.IsActive || _capturingRoot) return false;
        Interlocked.Increment(ref _liveRevision);
        return true;
    }

    /// <summary>Same safety contract as the desktop materialization guard, without FieldRefAccess/Emit.</summary>
    public static void PowerDynamicVarsPrefix(PowerModel __instance)
    {
        if (SimulationNotificationIsolation.IsActive && PowerDynamicVars.GetValue(__instance) == null)
            throw new InvalidOperationException(
                "Simulation attempted to initialize power display variables off-thread: " + __instance.Id.Entry);
    }

    private static void BuildUi(Node host)
    {
        _layer = new CanvasLayer { Name = "CombatSolverMobile", Layer = 90 };
        host.AddChild(_layer);
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _layer.AddChild(_root);
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var column = new VBoxContainer { Name = "SolverPanel", MouseFilter = Control.MouseFilterEnum.Stop };
        _root.AddChild(column);
        column.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        column.OffsetLeft = -560;
        column.OffsetRight = -30;
        column.OffsetTop = 130;
        column.AddThemeConstantOverride("separation", 8);
        _toggle = Button("Combat solver", 90);
        column.AddChild(_toggle);
        _toggle.Pressed += TogglePanel;
        _panel = new PanelContainer { Visible = false };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.045f, 0.065f, 0.075f, 0.97f),
            ContentMarginLeft = 16, ContentMarginRight = 16,
            ContentMarginTop = 14, ContentMarginBottom = 14,
            CornerRadiusTopLeft = 12, CornerRadiusTopRight = 12,
            CornerRadiusBottomLeft = 12, CornerRadiusBottomRight = 12,
        });
        column.AddChild(_panel);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 12);
        _panel.AddChild(body);
        var controls = new HBoxContainer();
        body.AddChild(controls);
        _search = Button("Search", 90);
        _cancel = Button("Cancel", 90);
        controls.AddChild(_search);
        controls.AddChild(_cancel);
        _search.Pressed += RequestSearch;
        _cancel.Pressed += CancelByUser;
        _cancel.Disabled = true;
        _optionsPanel = new MobileOptionsPanel(_options, options =>
        {
            _options = options;
            _options.Save();
            UpdateSearchCaption();
        });
        body.AddChild(_optionsPanel.Root);
        UpdateSearchCaption();
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(490, 270),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        body.AddChild(scroll);
        _text = new Label
        {
            Text = "Tap Search during your turn. Change the budget and potion policy in Options.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(480, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        _text.AddThemeFontSizeOverride("font_size", 23);
        scroll.AddChild(_text);
        _optionsPanel.ExpandedChanged += expanded => scroll.Visible = !expanded;
        // Clamp to the logical viewport; game content uses its own scaling.
        void Resize()
        {
            if (!IsValid(column)) return;
            Vector2 size = host.GetViewport().GetVisibleRect().Size;
            float width = Math.Clamp(size.X * 0.35f, 360, 530);
            column.OffsetLeft = -width - 30;
            scroll.CustomMinimumSize = new Vector2(width - 32, Math.Clamp(size.Y * 0.35f, 170, 330));
            _text!.CustomMinimumSize = new Vector2(width - 52, 0);
        }
        Resize();
        host.GetViewport().SizeChanged += Resize;
        _layer.TreeExiting += () =>
        {
            if (IsValid(host)) host.GetViewport().SizeChanged -= Resize;
        };
    }

    private static Button Button(string title, float height)
    {
        var button = new Button
        {
            Text = title, CustomMinimumSize = new Vector2(0, height),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            FocusMode = Control.FocusModeEnum.None,
        };
        button.AddThemeFontSizeOverride("font_size", 24);
        return button;
    }

    private static void TogglePanel()
    {
        if (IsValid(_panel)) _panel!.Visible = !_panel.Visible;
    }

    private static bool TryPlayableState(out CombatState? state)
    {
        state = CombatManager.Instance.DebugOnlyGetState();
        return CombatManager.Instance.IsInProgress && state is { Players.Count: 1 }
            && !RunManager.Instance.NetService.Type.IsMultiplayer()
            && state.CurrentSide == CombatSide.Player
            && LocalContext.GetMe(state)?.PlayerCombatState?.Phase == PlayerTurnPhase.Play
            && !CombatManager.Instance.PlayerActionsDisabled
            && RunManager.Instance.ActionExecutor.CurrentlyRunningAction == null
            && RunManager.Instance.ActionExecutor.FinishedExecutingActions().IsCompleted;
    }

    private static void RequestSearch()
    {
        if (_session != null)
        {
            SetText("The previous search is finishing. Please wait.");
            return;
        }
        if (!TryPlayableState(out CombatState? state))
        {
            SetText("Wait for your turn and finish any pending card choice or animation.");
            return;
        }
        if (MobileSearchPolicy.AvailableMemory() is ulong memory && memory < MobileSearchPolicy.AdmissionHeadroom)
        {
            SetText("Not enough available app memory to start a search safely. Try again later.");
            return;
        }
        _resultStamp = null;
        _optionsPanel?.Collapse();
        SetText("Capturing current combat…");
        try
        {
            VanillaRuntimeContract.ValidateCombatRoot(state!);
            // Capture all game/Godot data on the main thread. Upstream compares
            // the full live state before and after cloning, including RNG state.
            long revision = Volatile.Read(ref _liveRevision);
            SolverDisplayNames names;
            CombatRootSnapshot root;
            // Clone/materialization setters can emit notifications even when the
            // source state is unchanged. Upstream's before/after stamp is the
            // authority during this synchronous capture; no user action can run
            // concurrently on the game thread. Do not enable the worker-only
            // dynamic-variable guard while canonical data is being warmed.
            _capturingRoot = true;
            try
            {
                names = SolverDisplayNames.Capture(state!);
                root = CombatRootSnapshot.Capture(state!);
            }
            finally { _capturingRoot = false; }
            if (revision != Volatile.Read(ref _liveRevision))
                throw new InvalidOperationException("Combat changed while preparing the search; try again.");
            var session = new Session(state!, root.LiveStamp, revision);
            SearchPolicySnapshot policy = MobileSearchPolicy.Create(Frames, _options);
            session.BudgetMilliseconds = policy.BudgetOverrideMilliseconds!.Value;
            session.PotionPolicy = policy.PotionPolicy;
            // HP accounting starts at this request; suggestions describe further loss.
            var damage = new BattleDamageSnapshot(0, 0, 0, []);
            _session = session;
            _search!.Disabled = true;
            _cancel!.Disabled = false;
            SetText("Searching… You can cancel or continue playing.");
            session.Cancellation.CancelAfter(TimeSpan.FromMilliseconds(session.BudgetMilliseconds + 10_000));
            session.Worker = Task.Run(() =>
            {
                using IDisposable isolation = SimulationNotificationIsolation.Enter();
                return CombatSearchCoordinator.Solve(root, names, damage, policy,
                    session.Cancellation.Token, progress => Volatile.Write(ref session.Progress, progress));
            }, session.Cancellation.Token);
        }
        catch (Exception error)
        {
            SetText("Search unavailable: " + error.Message);
            Entry.Logger.Error("Mobile root capture failed: " + error);
            if (_session?.Worker == null)
            {
                _session?.Cancellation.Dispose();
                _session = null;
            }
        }
    }

    private static void OnFrame()
    {
        long now = Stopwatch.GetTimestamp();
        Frames.ObserveFrame((now - _lastFrameTicks) * 1000d / Stopwatch.Frequency, _session != null);
        _lastFrameTicks = now;
        long revision = Volatile.Read(ref _liveRevision);
        if (_session is { } session && session.Revision != revision)
            StopSearch("Combat changed. Search again when ready.");
        if (_resultStamp != null && _resultRevision != revision)
        {
            _resultStamp = null;
            SetText("Combat changed. Search again for a current recommendation.");
        }
    }

    private static void Tick()
    {
        try
        {
            bool inCombat = CombatManager.Instance.IsInProgress;
            CombatState? current = inCombat ? CombatManager.Instance.DebugOnlyGetState() : null;
            bool playable = TryPlayableState(out _);
            _root!.Visible = inCombat && current?.Players.Count == 1;
            if (!ReferenceEquals(current, _observedCombat))
            {
                _observedCombat = current;
                _resultStamp = null;
                StopSearch("Combat ended or changed.");
                SetText("Tap Search during your turn. Change the budget and potion policy in Options.");
            }
            _search!.Disabled = !playable || _session != null;
            _cancel!.Disabled = _session == null || _session.Cancellation.IsCancellationRequested;
            if (_session is not { } session)
            {
                if (_resultStamp != null && (!playable || LiveCombatStamp.Capture(current!) != _resultStamp))
                {
                    _resultStamp = null;
                    SetText("Combat changed. Search again for a current recommendation.");
                }
                return;
            }
            if (!playable || !ReferenceEquals(current, session.State))
                StopSearch("Combat changed. Search again when ready.");
            else if (!session.Cancellation.IsCancellationRequested && LiveCombatStamp.Capture(current!) != session.Stamp)
                StopSearch("Combat changed. Search again when ready.");
            else if (GC.GetTotalMemory(false) - session.ManagedBytesAtStart > MobileSearchPolicy.ManagedGrowthLimit
                || MobileSearchPolicy.AvailableMemory() is ulong remaining && remaining < MobileSearchPolicy.StopHeadroom)
                StopSearch("Search stopped to preserve memory. Try again after this action.");

            if (session.Worker is not { IsCompleted: true } completed)
            {
                if (!session.Cancellation.IsCancellationRequested && Volatile.Read(ref session.Progress) is { } progress)
                    SetText(MobileRouteText.FormatProgress(progress, session.BudgetMilliseconds));
                return;
            }
            _session = null; // The worker has drained before another can be admitted.
            try
            {
                if (session.Cancellation.IsCancellationRequested || completed.IsCanceled)
                {
                    _ = completed.Exception; // Observe a fault even when cancellation won the UI race.
                    SetText(session.StopReason ?? "Search timed out. Try a simpler combat state.");
                    return;
                }
                SolverResult result = completed.GetAwaiter().GetResult();
                if (!TryPlayableState(out current) || !ReferenceEquals(current, session.State)
                    || session.Revision != Volatile.Read(ref _liveRevision)
                    || LiveCombatStamp.Capture(current!) != session.Stamp)
                {
                    SetText("Result discarded: combat changed during the search.");
                    return;
                }
                SetText(MobileRouteText.Format(result, session.PotionPolicy));
                _resultStamp = session.Stamp;
                _resultRevision = session.Revision;
                Entry.Logger.Info($"Mobile search complete: nodes={result.TotalExpandedNodes}, seconds={session.Clock.Elapsed.TotalSeconds:F2}, boundary={result.BoundaryReason}");
            }
            finally { session.Cancellation.Dispose(); }
        }
        catch (Exception error)
        {
            StopSearch("Search unavailable: " + error.Message);
            SetText("Search unavailable: " + error.Message);
            Entry.Logger.Error("Mobile solver failed: " + error);
        }
    }

    private static void CancelByUser() => StopSearch("Search cancelled.");
    private static void OnFocusLost() => StopSearch("Search cancelled when the app lost focus.");

    private static void StopSearch(string reason)
    {
        if (_session is not { } session || session.Cancellation.IsCancellationRequested) return;
        session.StopReason = reason;
        session.Cancellation.Cancel();
        SetText(reason);
    }

    private static void Detach()
    {
        StopSearch("Combat view closed.");
        // A detached worker owns its cancellation until it drains. Never start a
        // second worker while this one is still running, including scene changes.
        if (IsValid(_tree)) _tree!.ProcessFrame -= OnFrame;
        if (IsValid(_window)) _window!.FocusExited -= OnFocusLost;
        if (IsValid(_host)) _host!.TreeExiting -= Detach;
        if (IsValid(_layer)) _layer!.QueueFree();
        _host = null;
        _tree = null;
        _window = null;
        _layer = null;
        _root = null;
        _panel = null;
        _optionsPanel = null;
        _text = null;
        _timer = null;
        _observedCombat = null;
        _resultStamp = null;
    }

    private static bool IsValid(GodotObject? value) => value != null && GodotObject.IsInstanceValid(value);
    private static void UpdateSearchCaption()
    {
        if (IsValid(_search)) _search!.Text = $"Search · {_options.Profile().SoftTimeBudgetMilliseconds / 1_000}s";
    }
    private static void SetText(string value) { if (IsValid(_text)) _text!.Text = value; }
}
