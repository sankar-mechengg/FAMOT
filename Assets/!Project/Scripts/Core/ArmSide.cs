namespace FAMOT.Core
{
    /// <summary>Which upper limb an object refers to.</summary>
    public enum ArmSide
    {
        Left = 0,
        Right = 1
    }

    public static class ArmSideExtensions
    {
        /// <summary>+1 for the right arm, -1 for the left arm. Multiplies body-frame "right" to obtain "lateral".</summary>
        public static float LateralSign(this ArmSide side) => side == ArmSide.Right ? 1f : -1f;

        public static ArmSide Opposite(this ArmSide side) => side == ArmSide.Right ? ArmSide.Left : ArmSide.Right;

        public static string ShortName(this ArmSide side) => side == ArmSide.Right ? "R" : "L";
    }
}
