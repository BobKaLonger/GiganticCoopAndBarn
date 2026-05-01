using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Objects;
using StardewValley.GameData.Buildings;
using System.Reflection;

namespace GiganticCoopAndBarn
{
    public class ModEntry : Mod
    {
        public static ModEntry? modInstance;
        internal const string GiganticCP = "bobkalonger.GiganticCoopAndBarnCP_";
        internal const string GigaBarn = $"{GiganticCP}GigaBarn";
        internal const string GigaCoop = $"{GiganticCP}GigaCoop";
        public override void Entry(IModHelper helper)
        {
            modInstance = this;

            var harmony = new Harmony(this.ModManifest.UniqueID);

            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        [HarmonyPatch(typeof(Utility), "_HasBuildingOrUpgrade")]
        public static class Utility_HasBuildingOrUpgrade_Patch
        {
            public static void Postfix(GameLocation location, string buildingId, ref bool __result)
            {
                if (__result) return;

                string? toCheck = null;
                if (buildingId == "Coop" || buildingId == "Big Coop" || buildingId == "Deluxe Coop")
                    toCheck = "ModEntry.GigaCoop";
                else if (buildingId == "Barn" || buildingId == "Big Barn" || buildingId == "Deluxe Barn")
                    toCheck = "ModEntry.GigaBarn";

                if (toCheck != null && location.getNumberBuildingsConstructed(toCheck) > 0)
                    __result = true;
            }
        }
    }
}