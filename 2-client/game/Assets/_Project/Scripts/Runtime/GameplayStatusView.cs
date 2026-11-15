namespace Pogranichye.Client
{
    /// <summary>
    /// Presence-aware values from one fast v1 frame. A missing property is different
    /// from an explicit JSON null: the former preserves the previous full-frame value,
    /// while the latter clears the legacy numeric selector to zero.
    /// </summary>
    public sealed class GameplayStatusPatch
    {
        public bool HasHealth { get; }
        public float Health { get; }
        public bool HasEnergy { get; }
        public float Energy { get; }
        public bool HasMana { get; }
        public float Mana { get; }
        public bool HasStamina { get; }
        public float Stamina { get; }

        public GameplayStatusPatch(
            bool hasHealth, float health,
            bool hasEnergy, float energy,
            bool hasMana, float mana,
            bool hasStamina, float stamina)
        {
            HasHealth = hasHealth;
            Health = health;
            HasEnergy = hasEnergy;
            Energy = energy;
            HasMana = hasMana;
            Mana = mana;
            HasStamina = hasStamina;
            Stamina = stamina;
        }
    }

    /// <summary>
    /// Read-only projection of server-owned character vitals used by the compact HUD.
    /// It is presentation state only: no local simulation or action outcome is derived here.
    /// </summary>
    public sealed class GameplayStatusView
    {
        public static readonly GameplayStatusView Empty = new GameplayStatusView(0f, 0f, 0f, 0f, 0f);

        public float Health { get; }
        public float Energy { get; }
        public float Mana { get; }
        public float Drunk { get; }
        public float Stamina { get; }

        private GameplayStatusView(float health, float energy, float mana, float drunk, float stamina)
        {
            Health = health;
            Energy = energy;
            Mana = mana;
            Drunk = drunk;
            Stamina = stamina;
        }

        public static GameplayStatusView From(
            double health, double energy, double mana, double drunk, double stamina)
        {
            return new GameplayStatusView(
                (float)health, (float)energy, (float)mana, (float)drunk, (float)stamina);
        }

        public GameplayStatusView Apply(GameplayStatusPatch patch)
        {
            if (patch == null) return this;
            return new GameplayStatusView(
                patch.HasHealth ? patch.Health : Health,
                patch.HasEnergy ? patch.Energy : Energy,
                patch.HasMana ? patch.Mana : Mana,
                Drunk,
                patch.HasStamina ? patch.Stamina : Stamina);
        }
    }
}
