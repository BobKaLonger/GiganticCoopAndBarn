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
        internal const string GiganticCP = "bobkalonger.gigacoopnbarn_";
        internal const string GigaBarn = $"{GiganticCP}GigaBarn";
        internal const string GigaCoop = $"{GiganticCP}GigaCoop";
        public override void Entry(IModHelper helper)
        {
            modInstance = this;

            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            helper.Events.GameLoop.DayStarted += OnDayStarted;
            helper.Events.Player.Warped += PlayerOnWarped;

            var harmony = new Harmony(this.ModManifest.UniqueID);

            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        private void MakeIncubatorsMoveable(AnimalHouse indoors)
        {
            foreach (var pair in indoors.Objects.Pairs)
            {
                StardewValley.Object obj = pair.Value;

                if (obj.QualifiedItemId == "(BC)101" && obj.questItem.Value)
                {
                    obj.questItem.Value = false;
                }
            }
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            foreach (Building building in Game1.getFarm().buildings)
            {
                if (building.indoors.Value is AnimalHouse indoors)
                {
                    MakeIncubatorsMoveable(indoors);
                }
            }
        }

        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            foreach (Building building in Game1.getFarm().buildings)
            {
                if (building.indoors.Value is AnimalHouse indoors)
                {
                    MakeIncubatorsMoveable(indoors);
                }
            }

            foreach (Building building in Game1.getFarm().buildings)
            {
                if (building.buildingType.Value is not (GigaBarn or GigaCoop))
                    continue;
                
                var interior = building.GetIndoors();

                if (building.daysUntilUpgrade.Value > 0 || interior == null) continue;

                string upgradeKey = $"{ModManifest.UniqueID}/buildingKey";
                string currentLevel = building.buildingType.Value;
                building.modData.TryGetValue(upgradeKey, out string lastMovedLevel);

                if (lastMovedLevel != currentLevel)
                {
                    if (building.buildingType.Value is GigaBarn)
                        BarnItemMoves(interior);
                    else if (building.buildingType.Value is GigaCoop)
                        CoopItemMoves(interior);
                    
                    building.modData[upgradeKey] = currentLevel;
                }
            }
        }

        private static List<(Vector2 tile, StardewValley.Object obj)> SpiralSearch(GameLocation location, string qualifiedID, Vector2 center, int maxRadius)
        {
            var results = new List<(Vector2, StardewValley.Object)>();

            for (int radius = 0; radius <= maxRadius; radius++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        if (Math.Abs(dx) != radius && Math.Abs(dy) != radius)
                            continue;
                        
                        Vector2 tile = new Vector2(center.X + dx, center.Y +dy);
                        if (location.objects.TryGetValue(tile, out StardewValley.Object obj) && obj.QualifiedItemId == qualifiedID)
                        {
                            results.Add((tile, obj));
                        }
                    }
                }
            }

            return results;
        }

        private static Vector2 LandingPadRect(GameLocation location, Rectangle landingPad)
        {
            for (int y = landingPad.Top; y < landingPad.Bottom; y++)
            {
                for (int x = landingPad.Left; x < landingPad.Right; x++)
                {
                    Vector2 candidate = new Vector2(x, y);
                    if (!location.IsTileBlockedBy(candidate, CollisionMask.Objects | CollisionMask.Furniture))
                    {
                        return candidate;
                    }
                }
            }
            return Vector2.Zero;
        }

        private static void BarnItemMoves(GameLocation interior)
        {
            if (interior.map == null) return;

            var namedDestinations = new Dictionary<string, Vector2>
            {
                { "(BC)99",  new Vector2(8, 3) },
            };

            var haySlots = new List<Rectangle>
            {
                new Rectangle(10, 3, 20, 1),
                new Rectangle(10, 12, 20, 1)
            };

            Vector2 startCenter = new Vector2(
                interior.map.Layers[0].LayerWidth / 2,
                interior.map.Layers[0].LayerHeight / 2
            );

            var foundHay = SpiralSearch(interior, "(O)178", startCenter, maxRadius: 50);

            foreach (var (sourceTile, obj) in foundHay)
                interior.removeObject(sourceTile, false);

            int haySlotIndex = 0;
            int haySlotX = haySlots[0].Left;

            foreach (var (sourceTile, obj) in foundHay)
            {
                if (haySlotIndex >= haySlots.Count) break;

                Vector2 dest = new Vector2(haySlotX, haySlots[haySlotIndex].Top);
                obj.TileLocation = dest;
                interior.objects[dest] = obj;

                haySlotX++;
                if (haySlotX >= haySlots[haySlotIndex].Right)
                {
                    haySlotIndex++;
                    if (haySlotIndex < haySlots.Count)
                        haySlotX = haySlots[haySlotIndex].Left;
                }
            }

            var excludedIds = new HashSet<string>(namedDestinations.Keys) { "(O)178" };

            foreach (var kvp in namedDestinations)
            {
                var found = SpiralSearch(interior, kvp.Key, startCenter, maxRadius: 50);

                if (found.Count == 0) continue;
                var (sourceTile, obj) = found[0];
                interior.removeObject(sourceTile, false);
                obj.TileLocation = kvp.Value;
                interior.objects[kvp.Value] = obj;
            }

            var landingPad = new Rectangle(x: 10, y: 14, width: 28, height: 14);

            var barnItemMoves = interior.objects.Pairs
                .Where(p => !excludedIds.Contains(p.Value.QualifiedItemId))
                .ToList();

            foreach (var pair in barnItemMoves)
            {
                Vector2 dest = LandingPadRect(interior, landingPad);
                if (dest == Vector2.Zero) continue;
                interior.removeObject(pair.Key, false);
                pair.Value.TileLocation = dest;
                interior.objects[dest] = pair.Value;
            }

            Vector2 correctFeederTile = namedDestinations["(BC)99"];
            var extraFeeders = SpiralSearch(interior, "(BC)99", startCenter, maxRadius: 50)
                .Where(f => f.tile != correctFeederTile)
                .ToList();
            foreach (var (tile, _) in extraFeeders)
                interior.removeObject(tile, false);
        }

        private static void CoopItemMoves(GameLocation interior)
        {
            if (interior.map == null) return;

            var namedDestinations = new Dictionary<string, Vector2>
            {
                { "(BC)99", new Vector2(6, 3) }
            };

            var haySlots = new List<Rectangle>
            {
                new Rectangle(8, 3, 20, 1),
                new Rectangle(8, 12, 20, 1)
            };

            Vector2 startCenter = new Vector2(
                interior.map.Layers[0].LayerWidth / 2,
                interior.map.Layers[0].LayerHeight / 2
            );

            var foundHay = SpiralSearch(interior, "(O)178", startCenter, maxRadius: 50);

            foreach (var (sourceTile, obj) in foundHay)
                interior.removeObject(sourceTile, false);
            
            int haySlotIndex = 0;
            int haySlotX = haySlots[0].Left;
            
            foreach (var (sourceTile, obj) in foundHay)
            {
                if (haySlotIndex >= haySlots.Count) break;

                Vector2 dest = new Vector2(haySlotX, haySlots[haySlotIndex].Top);
                obj.TileLocation = dest;
                interior.objects[dest] = obj;

                haySlotX++;
                if (haySlotX >= haySlots[haySlotIndex].Right)
                {
                    haySlotIndex++;
                    if (haySlotIndex < haySlots.Count)
                        haySlotX = haySlots[haySlotIndex].Left;
                }
            }

            Vector2[] incubatorDestinations =
            {
                new Vector2(5, 3),
                new Vector2(29, 3),
                new Vector2(30, 3)
            };

            var foundIncubators = SpiralSearch(interior, "(BC)101", startCenter, maxRadius: 50);

            for (int i = 0; i < foundIncubators.Count && i < incubatorDestinations.Length; i++)
            {
                var (sourceTile, obj) = foundIncubators[i];
                Vector2 dest = incubatorDestinations[i];
                interior.removeObject(sourceTile, false);
                obj.TileLocation = dest;
                interior.objects[dest] = obj;
            }

            for (int i = foundIncubators.Count; i < incubatorDestinations.Length; i++)
            {
                Vector2 dest = incubatorDestinations[i];

                if (interior.objects.TryGetValue(dest, out StardewValley.Object blocking))
                {
                    interior.objects.Remove(dest);
                    Game1.player.team.returnedDonations.Add(blocking);
                    Game1.player.team.newLostAndFoundItems.Value = true;
                }

                var newIncubator = ItemRegistry.Create("(BC)101") as StardewValley.Object;
                if (newIncubator != null)
                {
                    newIncubator.TileLocation = dest;
                    interior.objects[dest] = newIncubator;
                }
            }

            var excludedIds = new HashSet<string>(namedDestinations.Keys) { "(O)178", "(BC)101" };

            foreach (var kvp in namedDestinations)
            {
                var found = SpiralSearch(interior, kvp.Key, startCenter, maxRadius: 50);

                if (found.Count == 0) continue;
                var (sourceTile, obj) = found[0];
                interior.removeObject(sourceTile, false);
                obj.TileLocation = kvp.Value;
                interior.objects[kvp.Value] = obj;
            }

            var landingPad = new Rectangle(x: 3, y: 14, width: 31, height: 5);

            var coopItemMoves = interior.objects.Pairs
                .Where(p => !excludedIds.Contains(p.Value.QualifiedItemId))
                .ToList();

            foreach (var pair in coopItemMoves)
            {
                Vector2 dest = LandingPadRect(interior, landingPad);
                if (dest == Vector2.Zero) continue;
                interior.removeObject(pair.Key, false);
                pair.Value.TileLocation = dest;
                interior.objects[dest] = pair.Value;
            }

            Vector2 correctFeederTile = namedDestinations["(BC)99"];
            var extraFeeders = SpiralSearch(interior, "(BC)99", startCenter, maxRadius: 50)
                .Where(f => f.tile != correctFeederTile)
                .ToList();
            foreach (var (tile, _) in extraFeeders)
                interior.removeObject(tile, false);
        }

        [HarmonyPatch(typeof(NPC), "updateConstructionAnimation")]
        public static class RobinUpgradeBonk
        {
            public static void Postfix(NPC __instance)
            {
                if (!Game1.IsMasterGame) return;
                if (__instance.Name != "Robin") return;

                Building? building = null;

                foreach (var location in Game1.locations)
                {
                    building = location.buildings.FirstOrDefault(b =>
                            b.daysUntilUpgrade.Value > 0 &&
                            (b.upgradeName.Value == GigaBarn || b.upgradeName.Value == GigaCoop));
                    if (building != null) break;
                }

                if (building == null) return;

                GameLocation indoors = building.GetIndoors();
                if (building.daysUntilUpgrade.Value <= 0 || indoors == null) return;

                __instance.currentLocation?.characters.Remove(__instance);
                __instance.currentLocation = indoors;
                if (!indoors.characters.Contains(__instance))
                    indoors.addCharacter(__instance);

                __instance.setTilePosition(1, 5);
                __instance.ignoreScheduleToday = true;
            }
        }

        private void PlayerOnWarped(object? sender, WarpedEventArgs e)
        {
            if (e.NewLocation is AnimalHouse)
            {
                foreach (var building in Game1.getFarm().buildings)
                {
                    if (building.GetIndoors() == e.NewLocation &&
                        building.buildingType.Value is GigaBarn or GigaCoop)
                    {
                        Helper.GameContent.InvalidateCache(e.NewLocation.mapPath.Value);
                        e.NewLocation.reloadMap();
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Building), nameof(Building.FinishConstruction))]
        public static class InstantBuildingConstructionPatch
        {
            public static void Postfix(Building __instance)
            {
                if (__instance.buildingType.Value is not (GigaBarn or GigaCoop))
                    return;
                

                string upgradeKey = $"{modInstance!.ModManifest.UniqueID}/buildingKey";
                string currentLevel = __instance.buildingType.Value;

                __instance.modData.TryGetValue(upgradeKey, out string lastMovedLevel);
                if (lastMovedLevel == currentLevel)
                    return;

                modInstance!.Helper.Events.GameLoop.UpdateTicked += DoItemMoves;

                void DoItemMoves(object? sender, UpdateTickedEventArgs e)
                {
                    if (!e.IsMultipleOf(3)) return;
                    modInstance!.Helper.Events.GameLoop.UpdateTicked -= DoItemMoves;

                    GameLocation interior = __instance.GetIndoors();
                    if (interior == null || interior.map == null)
                        return;

                    if (__instance.buildingType.Value is GigaBarn)
                        BarnItemMoves(interior);
                    else if (__instance.buildingType.Value is GigaCoop)
                        CoopItemMoves(interior);

                    
                    if (interior is AnimalHouse animalHouse)
                        modInstance!.MakeIncubatorsMoveable(animalHouse);

                    __instance.modData[upgradeKey] = currentLevel;
                }
            }
        }

        [HarmonyPatch(typeof(Utility), "_HasBuildingOrUpgrade")]
        public static class Utility_HasBuildingOrUpgrade_Patch
        {
            public static void Postfix(GameLocation location, string buildingId, ref bool __result)
            {
                if (__result) return;

                string? toCheck = null;
                if (buildingId == "Coop" || buildingId == "Big Coop" || buildingId == "Deluxe Coop")
                    toCheck = GigaCoop;
                else if (buildingId == "Barn" || buildingId == "Big Barn" || buildingId == "Deluxe Barn")
                    toCheck = GigaBarn;

                if (toCheck != null && location.getNumberBuildingsConstructed(toCheck) > 0)
                    __result = true;
            }
        }

        [HarmonyPatch(typeof(Building), nameof(Building.GetData))]
        public static class GiganticSignPatch
        {
            public static void Postfix(Building __instance, ref BuildingData __result)
            {
                if (__result == null)
                    return;
                
                if (__instance.upgradeName.Value is not (GigaBarn or GigaCoop))
                    return;

                switch (__instance.upgradeName.Value)
                {
                    case GigaBarn:
                        __result.UpgradeSignTile = new Vector2(4.5f, 4f);
                        __result.UpgradeSignHeight = 50f;
                        break;
                    case GigaCoop:
                        __result.UpgradeSignTile = new Vector2(4.5f, 4f);
                        __result.UpgradeSignHeight = 52f;
                        break;
                }
            }
        }
    }
}