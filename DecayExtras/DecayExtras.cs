using System;
using System.Reflection;
using Harmony;
using Rust;
using UnityEngine;
using SplashUtilities;

namespace DecayExtras
{
    [HarmonyPatch(typeof(Bootstrap), "StartServer")]
    public class DecayExtrasPatch
    {
        public static void Postfix()
        {
            DecayExtras.Init();
        }
    }
    
    [HarmonyPatch(typeof(DeployableDecay), "GetDecayDelay", typeof(BaseEntity))]
    public class DeployableDecayPatch
    {
        public static bool Prefix(BaseEntity entity, ref float __result)
        {
            if (!DecayExtras.deployableGlobalOverride) return true;

            __result = DecayExtras.deployableDecayDelay;
            return false;
        }
    }

    [HarmonyPatch(typeof(DeployableDecay), "GetDecayDuration", typeof(BaseEntity))]
    public class DeployableDecayDurationPatch
    {
        public static bool Prefix(BaseEntity entity, ref float __result)
        {
            if (!DecayExtras.deployableGlobalOverride) return true;

            __result = DecayExtras.deployableDecayDuration;
            return false;
        }
    }
 
    [HarmonyPatch(typeof(DeployableDecay), "ShouldDecay", typeof(BaseEntity))]
    public class DeployableDecayShouldDecayPatch
    {
        public static bool Prefix(BaseEntity entity, ref bool __result)
        {
            if (!DecayExtras.deployableGlobalOverride) return true;
            if (Array.IndexOf(DecayExtras.deployableDecayBlacklist, entity.ShortPrefabName) == -1) return true;
            
            __result = false;
            return false;
        }
    }
    
    public static class DecayExtras
    {
        [ServerVar(Help = "Enables global deployable decay override (default false)")]
        public static bool deployableGlobalOverride = false;
        
        [ServerVar(Help = "Overrides the global deployable decay delay (default 8 hours)")]
        public static float deployableDecayDelay = 8 * 60 * 60;
        
        [ServerVar(Help = "Overrides the global deployable decay duration (default 8 hours)")]
        public static float deployableDecayDuration = 8 * 60 * 60;
        
        [ServerVar(Help = "Comma separated list of deployable types to disable decay for")]
        public static string[] deployableDecayBlacklist = {};
        
        public static BuildingManager.Building LookingAtBuilding(BasePlayer player)
        {
            RaycastHit hit;
            if (!Physics.Raycast(player.eyes.HeadRay(), out hit, 5f, Layers.Construction)) return null;
            
            var ent = hit.GetEntity() as DecayEntity;
            return ent == null ? null : ent.GetBuilding();
        }
        
        public static void Init()
        {
            RustUtils.RegisterCommands(new []
            {
                RustUtils.CreateCommand("reload", DecayReload, true, "decay"),
                RustUtils.CreateCommand("showconfig", DecayShowConfig, true, "decay"),
                RustUtils.CreateCommand("showblockcount", ShowBlockCount, true, "decay"),
                RustUtils.CreateCommand("showbuildings", ShowBuildings, true, "decay"),
                RustUtils.CreateCommand("showtax", ShowBuildingTaxRate, true, "decay"),
                RustUtils.CreateCommand("teleport2building", TeleportToBuildingId, true, "decay"),
                RustUtils.CreateVariable("deployable_duration", () => deployableDecayDuration.ToString(), delegate(string str) { deployableDecayDuration = float.Parse(str); }, "decay"),
                RustUtils.CreateVariable("deployable_delay", () => deployableDecayDelay.ToString(), delegate(string str) { deployableDecayDelay = float.Parse(str); }, "decay"),
                RustUtils.CreateVariable("deployable_global_override", () => deployableGlobalOverride.ToString(), delegate(string str) { deployableGlobalOverride = bool.Parse(str); }, "decay"),
                RustUtils.CreateVariable("deployable_blacklist", () => string.Join(",", deployableDecayBlacklist), delegate(string str) { deployableDecayBlacklist = str.Split(','); }, "decay")
            });
            
            // Re-read the configuration file so we pick up the new values for our newly registered convars.
            ConVar.Server.readcfg(null);
        }

        public static void TeleportToBuildingId(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !player.IsAdmin) return;
            
            var buildingId = arg.GetUInt(0, 0);
            if (buildingId == 0)
            {
                arg.ReplyWith("Invalid building id");
                return;
            }
            
            var building = BuildingManager.server.GetBuilding(buildingId);
            if (building == null)
            {
                arg.ReplyWith($"Building {buildingId} not found");
                return;
            }

            if (building.HasBuildingPrivileges())
            {
                var priv = building.GetDominatingBuildingPrivilege();
                player.Teleport(priv.transform.position);
                arg.ReplyWith($"Teleported to building {buildingId} main tool cupboard.");
                return;
            }

            if (building.HasBuildingBlocks())
            {
                var block = building.buildingBlocks[0];
                player.Teleport(block.transform.position);
                arg.ReplyWith($"Teleported to building {buildingId} first block.");
                return;
            }

            if (building.HasDecayEntities())
            {
                var entity = building.decayEntities[0];
                player.Teleport(entity.transform.position);
                arg.ReplyWith($"Teleported to building {buildingId} first entity.");
                return;
            }
            
            arg.ReplyWith($"Building {buildingId} is empty.. no blocks, privs, or decay entities.");
        }
        
        // Show the building tax for the given building ID or building looking at
        public static void ShowBuildingTaxRate(ConsoleSystem.Arg arg)
        {
            var buildingId = arg.GetUInt(0);
            var player = arg.Player();
            if (player == null && buildingId == 0)
            {
                arg.ReplyWith("Must specify building ID or look at a building");
                return;
            }

            if (player != null && !player.IsAdmin) return;

            var building = buildingId == 0 ? LookingAtBuilding(player) : BuildingManager.server.GetBuilding(buildingId);
            
            if (building == null)
            {
                arg.ReplyWith($"Unable to find building.");
                return;
            }

            var tax = building.GetDominatingBuildingPrivilege()?.CalculateUpkeepCostFraction() ?? 0;

            arg.ReplyWith($"Building {building.ID} tax rate: {tax}");
        }

        // Show the blockcount of the building we're looking at.
        public static void ShowBlockCount(ConsoleSystem.Arg arg)
        {
            if (arg.Player() == null)
            {
                arg.ReplyWith("Must be run from in-game while looking at a building.");
                return;
            }

            if (!arg.Player().IsAdmin) return;
            var player = arg.Player();
            
            var building = LookingAtBuilding(player);
            if (building == null)
            {
                arg.ReplyWith("Must be looking at a building or decay entity part of a building.");
                return;
            }
                
            arg.ReplyWith($"building {building.ID} has {building.buildingBlocks.Count} blocks");
        }
        
        // List all the buildings and information for them.
        public static void ShowBuildings(ConsoleSystem.Arg arg)
        {
            if (arg.Player() != null && !arg.Player().IsAdmin) return;

            var table = new TextTable();
            
            table.AddColumns("ID", "Privileges", "Blocks", "DecayEntities", "NavMeshCarvers", "NavMeshOptimized");

            var bdField = typeof(BuildingManager).GetField("buildingDictionary");
            var buildingDict = (ListDictionary<uint, BuildingManager.Building>)bdField.GetValue(BuildingManager.server);

            foreach (var building in buildingDict.Values)
            {
                table.AddRow(building.ID.ToString(), building.buildingPrivileges.Count.ToString(),
                    building.buildingBlocks.Count.ToString(), building.decayEntities.Count.ToString(), 
                    building.navmeshCarvers == null ? "null" : building.navmeshCarvers.Count.ToString(), 
                    building.isNavMeshCarveOptimized.ToString());
            }
            
            arg.ReplyWith(table.ToString());
        }
        
        // Show all the decay configuration values
        public static void DecayShowConfig(ConsoleSystem.Arg arg)
        {
            string reply = "All decay settings:\n";
            
            foreach (var command in ConsoleSystem.Index.Server.Dict.Values)
            {
                if (!command.Variable) continue;
                if (command.Parent != "decay") continue;

                reply += $"  {command.FullName}: \"{command.GetOveride()}\"\n";
            }

            arg.ReplyWith(reply);
        }
        
        public static void DecayReload(ConsoleSystem.Arg arg)
        {
            var upkeepBrackets = new BuildingPrivlidge.UpkeepBracket[4]
            {
                new BuildingPrivlidge.UpkeepBracket(ConVar.Decay.bracket_0_blockcount, ConVar.Decay.bracket_0_costfraction),
                new BuildingPrivlidge.UpkeepBracket(ConVar.Decay.bracket_1_blockcount, ConVar.Decay.bracket_1_costfraction),
                new BuildingPrivlidge.UpkeepBracket(ConVar.Decay.bracket_2_blockcount, ConVar.Decay.bracket_2_costfraction),
                new BuildingPrivlidge.UpkeepBracket(ConVar.Decay.bracket_3_blockcount, ConVar.Decay.bracket_3_costfraction)
            };

            var field = typeof(BuildingPrivlidge).GetField("upkeepBrackets", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
            if (field == null)
            {
                Debug.LogWarning("[DecayExtras] Unable to access upkeepBrackets in BuildingPrivilege");
                return;
            }
            
            field.SetValue(null, upkeepBrackets);
            Debug.Log("[DecayExtras] Updated decay convar values.");
        }
    }
}