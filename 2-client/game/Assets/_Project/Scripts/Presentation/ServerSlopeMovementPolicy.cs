namespace Pogranichye.Client.Presentation
{
    /// <summary>
    /// Mirrors sim.SlopeMult from 1-server/sim/slope.go. Inputs and outputs are unitless grades
    /// and speed multipliers; the server remains authoritative for movement.
    /// </summary>
    public static class ServerSlopeMovementPolicy
    {
        public static double Multiplier(double grade, double uphillFactor, double downhillFactor,
            double easyDownGrade, double easyDownBoost, double cliffGrade)
        {
            if (grade > cliffGrade) return 0d;
            if (grade > 0d) return 1d / (1d + uphillFactor * grade);
            if (grade > -easyDownGrade)
            {
                if (easyDownGrade <= 0d) return 1d;
                return 1d + (easyDownBoost - 1d) * (-grade / easyDownGrade);
            }
            return easyDownBoost /
                (1d + downhillFactor * (-grade - easyDownGrade));
        }
    }
}
