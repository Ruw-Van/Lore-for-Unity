namespace Lore.Unity.Application.Runtime
{
    public enum RuntimeAvailability
    {
        SetupRequired,
        UnsupportedPlatform,
        Ready
    }

    public sealed class RuntimeContext
    {
        public RuntimeContext(RuntimeAvailability availability)
        {
            Availability = availability;
        }

        public RuntimeAvailability Availability { get; }
    }
}
