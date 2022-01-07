using BepInEx;
using BepInEx.Logging;
using System;
using DiskCardGame;
using HarmonyLib;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using APIPlugin;

namespace ModdedSaveFixer
{

    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("cyantist.inscryption.api", BepInDependency.DependencyFlags.HardDependency)]
    public class Plugin : BaseUnityPlugin
    {
        private const string PluginGuid = "IngoH.inscryption.ModdedSaveFixer";
        private const string PluginName = "ModdedSaveFixer";
        private const string PluginVersion = "1.1.0";

        internal static ManualLogSource Log;

        private void Awake()
        {
            Logger.LogInfo($"Loaded {PluginName}!");
            Plugin.Log = base.Logger;

            Harmony harmony = new Harmony(PluginGuid);
            harmony.PatchAll();

        }

        [HarmonyPatch(typeof(StartScreenController), "Start")]
        public class StartupPatch : StartScreenController
        {
            [HarmonyBefore("IngoH.inscryption.SkipStartScreen")]
            public static bool Prefix(StartScreenController __instance)
            {
                if (!startedGame)
                {
                    FixSave();
                }
                return true;
            }
        }


        private static void FixSave()
        {
            Plugin p = new Plugin();
            String path = Path.Combine(p.Info.Location.Replace("ModdedSaveFixer.dll", ""), "SaveFile.aux");

            Dictionary<int, string> storedAbilities = new Dictionary<int, string>();
            Dictionary<int, string> currentAbilities = new Dictionary<int, string>();
            Dictionary<int, string> removedAbilities = new Dictionary<int, string>();
            Dictionary<int, int> movedAbilities = new Dictionary<int, int>();

            Dictionary<NewAbility, string> discrepancies = new Dictionary<NewAbility, string>();

            // Read stored abilities
            if (File.Exists(path))
            {
                string[] existing = File.ReadAllText(path).Replace("\r", "").Split('\n');
                foreach (string ab in existing)
                {
                    if (ab.Contains(";")) storedAbilities.Add(int.Parse(ab.Split(new char[] { ';' }, 2)[0]), ab.Split(new char[] { ';' }, 2)[1]);
                }
            }

            // Read modded abilities
            for (int i = 0; i < NewAbility.abilities.Count; i++)
            {
                NewAbility na = NewAbility.abilities[i];
                Ability ability = na.ability;
                int index = (int) ability;
                string name;
                if (na.id != null)
                {
                    name = na.id.ToString();
                }
                else
                {
                    if (na.info.rulebookName != null) name = na.info.rulebookName;
                    else name = na.info.name;
                }

                if (!storedAbilities.ContainsKey(index))
                {
                    // If not stored, add
                    storedAbilities.Add(index, name);
                    Log.LogInfo("New ability found: " + name);
                }
                else if (storedAbilities[index] != name)
                {
                    // If stored but name is different, add as discrepancy.
                    if (ProgressionData.Data.learnedAbilities.Contains((Ability) index)) discrepancies.Add(na, storedAbilities[index]);
                }
                currentAbilities.Add(index, name);
            }

            foreach (KeyValuePair<NewAbility, string> pair in discrepancies)
            {
                NewAbility na = pair.Key;
                Ability ability = na.ability;
                int index = (int) ability;
                string oldName = pair.Value;
                string name;
                if (na.id != null)
                {
                    name = na.id.ToString();
                }
                else
                {
                    if (na.info.rulebookName != null) name = na.info.rulebookName;
                    else name = na.info.name;
                }

                if (currentAbilities.ContainsValue(oldName))
                {
                    ProgressionData.Data.learnedAbilities.Remove(ability);
                    int key = currentAbilities.FirstOrDefault(x => x.Value == oldName).Key;
                    if (key > 99)
                    {
                        movedAbilities.Add(index, key);
                        ProgressionData.Data.learnedAbilities.Add((Ability) key);
                        storedAbilities[index] = name;
                        Log.LogWarning("Ability discrepancy detected: " + name + " is now stored at index " + index + " (was " + oldName + "). The learned ability " + "(" + oldName + ") has been moved to the correct index (" + key + ")");
                    }
                    else
                    {
                        Log.LogError("Ability discrepancy detected: " + name + " is now stored at index " + index + " (was " + oldName + "). Tried to move the learned ability, but failed. The learned ability " + "(" + oldName + ") has been removed from the save file.");
                    }
                }
                else
                {
                    storedAbilities[index] = name;
                    ProgressionData.Data.learnedAbilities.Remove(ability);
                    Log.LogWarning("Ability discrepancy detected: " + name + " is now stored at index " + index + " (was " + oldName + "). The learned ability " + "(" + oldName + ") has been removed from the save file.");
                }
            }

            foreach (KeyValuePair<int, string> pair in storedAbilities)
            {
                if (!currentAbilities.ContainsValue(pair.Value))
                {
                    ProgressionData.Data.learnedAbilities.Remove((Ability) pair.Key);
                    Log.LogWarning("Ability removed: " + pair.Value + ". The learned ability " + "(" + pair.Key + ") has been removed from the save file.");
                }
            }

            // Save auxiliary file
            string output = "";
            foreach (KeyValuePair<int, string> pair in currentAbilities)
            {
                if (output != "") output += "\r\n";
                output += pair.Key + ";" + pair.Value;
            }
            File.WriteAllText(path, output);

            foreach (KeyValuePair<int, string> pair in removedAbilities)
            {
                int index = pair.Key;
                Ability ability = (Ability) pair.Key;
                if (pair.Key > 99 && ProgressionData.Data.learnedAbilities.Contains(ability))
                {
                    ProgressionData.Data.learnedAbilities.Remove(ability);
                    Log.LogWarning("Removing invalid ability from save file: " + index + " (originally " + pair.Value + ")");
                }
            }

            // Remove all removed abilities from save file
            for (int i = ProgressionData.Data.learnedAbilities.Count - 1; i >= 0; i--)
            {
                Ability ability = ProgressionData.Data.learnedAbilities[i];
                int index = (int) ability;
                if ((index > 99 || index == 0) && !currentAbilities.ContainsKey(index))
                {
                    ProgressionData.Data.learnedAbilities.Remove(ability);
                    Log.LogWarning("Removing invalid ability from save file: " + index + " (original name is unknown - it was likely removed before installing this mod)");
                }
            }

            // Clear invalid normal cards from deck
            if (RunState.Run != null)
            {
                if (RunState.Run.playerDeck != null)
                {
                    List<string> cardIds = RunState.Run.playerDeck.cardIds;
                    for (int i = cardIds.Count - 1; i >= 0; i--)
                    {
                        if (!ScriptableObjectLoader<CardInfo>.AllData.Exists(card => card.name == cardIds[i]) && !APIPlugin.NewCard.cards.Exists(card => card.name == cardIds[i]))
                        {
                            Log.LogWarning("Removing invalid card from deck: " + cardIds[i]);
                            cardIds.RemoveAt(i);
                        }
                    }
                    List<List<CardModificationInfo>> infos = RunState.Run.playerDeck.cardIdModInfos.Values.ToList();
                    for (int i = infos.Count - 1; i >= 0; i--)
                    {
                        foreach (CardModificationInfo info in infos[i]) {
                            foreach (int moved in info.abilities.Where(mod => movedAbilities.ContainsKey((int) mod)).ToArray())
                            {
                                info.abilities.Add((Ability) movedAbilities[moved]);
                                info.abilities.Remove((Ability) moved);
                                Log.LogWarning($"Modification info ability discrepancy detected: ability {moved} moved to {movedAbilities[moved]}");
                            }
                        }
                        if (infos[i].Exists(mod => mod.abilities.Exists(ability => (int) ability >= ((int) Ability.NUM_ABILITIES + NewAbility.abilities.Count)))) {
                            infos.RemoveAt(i);
                            Log.LogWarning("Removing invalid modification info");
                        }
                    }
                }

                List<Ability> abilities = RunState.Run.totemBottoms;
                for (int i = abilities.Count - 1; i >= 0; i--)
                {
                    if (movedAbilities.ContainsKey((int) abilities[i]))
                    {
                        Log.LogWarning($"Totem bottom ability discrepancy detected: ability {(int) abilities[i]} moved to {movedAbilities[(int) abilities[i]]}");
                        abilities[i] = (Ability) movedAbilities[(int) abilities[i]];
                    } else if ((int) abilities[i] >= ((int) Ability.NUM_ABILITIES + NewAbility.abilities.Count)) {
                        Log.LogWarning($"Removing invalid totem bottom with ability {abilities[i]}");
                        abilities.RemoveAt(i);
                    }
                }
                if (RunState.Run.totems.Count == 1)
                {
                    int ability = (int) RunState.Run.totems[0].ability;
                    if (movedAbilities.ContainsKey(ability))
                    {
                        Log.LogWarning($"Current totem ability discrepancy detected: ability {ability} moved to {movedAbilities[ability]}");
                        RunState.Run.totems[0].ability = (Ability) movedAbilities[ability];
                    }
                    else if(ability >= ((int) Ability.NUM_ABILITIES + NewAbility.abilities.Count))
                    {
                        Log.LogWarning($"Removing invalid current totem with ability {ability}");
                        RunState.Run.totems.RemoveAt(0);
                    }
                }
            }

            // Clear invalid death cards
            if (SaveManager.SaveFile != null)
            {
                List<CardModificationInfo> mods = SaveManager.SaveFile.deathCardMods;
                for (int i = mods.Count - 1; i >= 0; i--) {
                    foreach (int moved in mods[i].abilities.Where(mod => movedAbilities.ContainsKey((int) mod)).ToArray())
                    {
                        mods[i].abilities.Add((Ability) movedAbilities[moved]);
                        mods[i].abilities.Remove((Ability) moved);
                        Log.LogWarning($"Death Card ability discrepancy detected for {mods[i].nameReplacement}: ability {moved} moved to {movedAbilities[moved]}");
                    }
                    if (mods[i].abilities.Exists(sa => (int) sa >= ((int) Ability.NUM_ABILITIES + NewAbility.abilities.Count)))
                    {
                        mods.RemoveAt(i);
                        Log.LogWarning($"Removing invalid Death Card: {mods[i].nameReplacement}");
                        continue;
                    }
                }
            }

            // Save save file
            SaveManager.SaveToFile(false);

            //storedAbilities = currentAbilities;
        }
    }
}
