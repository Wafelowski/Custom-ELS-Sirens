using Rage;
using Rage.Native;

namespace CustomELSSirens
{
    // Invoked only for an input edge on the game fiber.
    internal sealed class NativeVehicleExtras : IVehicleExtras
    {
        private readonly Vehicle vehicle;
        internal NativeVehicleExtras(Vehicle vehicle) { this.vehicle = vehicle; }
        public bool Exists(int extra) => NativeFunction.Natives.DOES_EXTRA_EXIST<bool>(vehicle, extra);
        public bool IsEnabled(int extra) => NativeFunction.Natives.IS_VEHICLE_EXTRA_TURNED_ON<bool>(vehicle, extra);
        public void SetEnabled(int extra, bool enabled)
        {
            // GTA's third argument is 'disable': true turns the extra OFF.
            NativeFunction.Natives.SET_VEHICLE_EXTRA(vehicle, extra, !enabled);
        }
    }
}
