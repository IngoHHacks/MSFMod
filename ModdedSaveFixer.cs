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
        private const string PluginVersion = "1.0.0";

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
                int index = (int)ability;
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
                    discrepancies.Add(na, storedAbilities[index]);
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
                    bool removed = false;
                    if (ProgressionData.Data.learnedAbilities.Contains(ability))
                    {
                        ProgressionData.Data.learnedAbilities.Remove(ability);
                        removed = true;
                    }
                    int key = currentAbilities.FirstOrDefault(x => x.Value == oldName).Key;
                    if (key > 99)
                    {
                        ProgressionData.Data.learnedAbilities.Add((Ability)key);
                        storedAbilities[index] = name;
                        if (removed) Log.LogWarning("Ability discrepancy detected: " + name + " is now stored at index " + index + " (was " + oldName + "). The learned ability " + "(" + oldName + ") has been moved to the correct index (" + key + "). The save file has been modified to reflect this change.");
                        else Log.LogWarning("Ability discrepancy detected: " + name + " is now stored at index " + index + " (was " + oldName + "). The learned ability " + "(" + oldName + ") has been moved to the correct index (" + key + ")");
                    }
                    else
                    {
                        if (removed) Log.LogError("Ability discrepancy detected: " + name + " is now stored at index " + index + " (was " + oldName + "). Tried to move the learned ability, but failed. The learned ability " + "(" + oldName + ") has been removed from the auxiliary file and save file.");
                        else Log.LogError("Ability discrepancy detected: " + name + " is now stored at index " + index + " (was " + oldName + "). Tried to move the learned ability, but failed. The learned ability " + "(" + oldName + ") has been removed from the auxiliary file.");
                    }
                }
                else
                {
                    storedAbilities[index] = name;
                    if (ProgressionData.Data.learnedAbilities.Contains(ability)) {
                        ProgressionData.Data.learnedAbilities.Remove(ability);
                        Log.LogWarning("Ability discrepancy detected: " + name + " is now stored at index " + index + " (was " + oldName + "). The learned ability " + "(" + oldName + ") has been removed from the auxiliary file and save file.");
                    } else
                    {
                        Log.LogWarning("Ability discrepancy detected: " + name + " is now stored at index " + index + " (was " + oldName + "). The learned ability " + "(" + oldName + ") has been removed from the auxiliary file.");
                    }
                }
            }

            foreach (KeyValuePair<int, string> pair in storedAbilities)
            {
                if (!currentAbilities.ContainsValue(pair.Value))
                {
                    if (ProgressionData.Data.learnedAbilities.Contains((Ability)pair.Key))
                    {
                        ProgressionData.Data.learnedAbilities.Remove((Ability)pair.Key);
                        Log.LogWarning("Ability removed: " + pair.Value + ". The learned ability " + "(" + pair.Key + ") has been removed from the auxiliarey file and save file.");
                    }
                    else
                    {
                        Log.LogInfo("Ability removed: " + pair.Value + ". The learned ability " + "(" + pair.Key + ") has been removed from the auxiliary file.");
                    }
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
                Ability ability = (Ability)pair.Key;
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
                int index = (int)ability;
                if (index > 99 && !currentAbilities.ContainsKey(index))
                {
                    ProgressionData.Data.learnedAbilities.Remove(ability);
                    Log.LogWarning("Removing invalid ability from save file: " + index + " (original name is unknown - it was likely removed before installing this mod)");
                }
            }

            // Save save file
            SaveManager.SaveToFile(false);

            //storedAbilities = currentAbilities;
        }
    }
}
