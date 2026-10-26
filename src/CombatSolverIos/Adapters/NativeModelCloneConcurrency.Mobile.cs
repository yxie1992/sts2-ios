using MegaCrit.Sts2.Core.Models;

namespace CombatSolver.Engine.Common;

internal static class NativeModelCloneConcurrency
{
    public static bool CanCloneIndependently(AbstractModel source)
    {
        VanillaRuntimeContract.ValidateModel(source);
        // Disable the desktop metadata-specific fast path. The caller retains
        // its normal BaseLib gate when needed; unmodified native clones are not
        // globally serialized by this decision. This build installs none of the
        // external metadata patches required to certify that optional fast path.
        return false;
    }
}
