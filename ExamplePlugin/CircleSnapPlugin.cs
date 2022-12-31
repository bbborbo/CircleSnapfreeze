using BepInEx;
using BepInEx.Configuration;
using EntityStates;
using R2API;
using R2API.Utils;
using RoR2;
using RoR2.Projectile;
using RoR2.Skills;
using System;
using UnityEngine;
using RTAutoSprintEx;
using System.Runtime.CompilerServices;

namespace CircleSnapfreeze
{
    [BepInDependency(R2API.R2API.PluginGUID)]
    //Soft Dependencies are good for intercompatability, allowing our mod to load after AutoSprint if it's installed, but without requiring it to be installed for CircleSnapfreeze to run at all.
    [BepInDependency("com.johnedwa.RTAutoSprintEx", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [R2APISubmoduleDependency(nameof(LanguageAPI))]
	
	public class CircleSnapPlugin : BaseUnityPlugin
	{
        //The Plugin GUID should be a unique ID for this plugin, which is human readable (as it is used in places like the config).
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "Borbo";
        public const string PluginName = "CircleSnapfreeze";
        public const string PluginVersion = "1.1.1";

        //Making a bool to check if AutoSprint is loaded for easy access later
        bool isAutosprintLoaded = Tools.isLoaded("com.johnedwa.RTAutoSprintEx");

        #region assets
        // The AssetBundle here is something that's literally only needed for Clapfreeze assets (just the icon). I hate that I have to do it this way but oh well.
        // I wont be explaining AssetBundles since its not really worth it for this one icon but I can explain it at a later date
        public static AssetBundle iconBundle = Tools.LoadAssetBundle(Properties.Resources.clapfreeze);
        public static string iconsPath = "Assets/";
        #endregion

        #region config
        // config sux
        internal static ConfigFile CustomConfigFile { get; set; }
        public static ConfigEntry<float> CircleMaxRadius { get; set; }
        public static ConfigEntry<float> CircleMaxDeployTime { get; set; }
        public static ConfigEntry<int> TotalRays { get; set; }
        public static ConfigEntry<int> PillarsPerRay { get; set; }
        public static ConfigEntry<int> RayRotationOffset { get; set; }
        public static ConfigEntry<bool> UseClapfreezeAssets { get; set; }
        #endregion

        public static GameObject CircleSnapWalkerPrefab;
        public void Awake()
        {
            // Init our logging class so that we can properly log for debugging
            Log.Init(Logger);

            // Initializing config first so that we can retrieve config values for circle snap before we do anything else.
            InitializeConfig();

            // Then we need to create our own version of the "walker" projectile so that we can get it to behave how we want it to.
            InitializeCustomWalker();

            // Finally, we can replace the entity state for Snapfreeze.
            CircleTheSnapfreeze();

            //Checking if AutoSprint is loaded before doing compatability operations
            if (isAutosprintLoaded)
            {
                //Calling a separate method for intercompatability allows us to use extra logic to prevent our mod from breaking if the other mod isnt installed
                DoAutosprintCompat();
            }

            // After everything is done, all that's left to do is initialize our Content Pack.
            new ContentPacks().Initialize();

            Log.LogInfo(nameof(Awake) + " done.");
        }

        //This essentially stops this method from breaking our mod if it's unable to run the code inside due to intercompat
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        private void DoAutosprintCompat()
        {
            SendMessage("RT_SprintDisableMessage", "CircleSnapfreeze.States.PrepCircleWall");
        }

        private void InitializeConfig()
        {
            CustomConfigFile = new ConfigFile(Paths.ConfigPath + "\\CircleSnap.cfg", true);

            CircleMaxRadius = CustomConfigFile.Bind<float>("Circle Snap", "Snapfreeze Maximum Radius", 6,
                "This determines the outer radius of Circle Snapfreeze's Snapfreeze Circle. All ice pillars spawned will be WITHIN this radius. " +
                "12 behaves the same as vanilla.");

            CircleMaxDeployTime = CustomConfigFile.Bind<float>("Circle Snap", "Snapfreeze Deploy Time", 0.15f,
                "This determines the time it takes to fully deploy Snapfreeze. " +
                "0.3 behaves the same as vanilla.");

            TotalRays = CustomConfigFile.Bind<int>("Circle Snap", "Total Rays", 9,
                "This determines how many 'rays' circle snapfreeze has. " +
                "2 rays behaves the same as vanilla. Minimum of 2 rays.");
            TotalRays.Value = Mathf.Max(TotalRays.Value, 2); //To set a "minimum" value, we have to get the higher number.

            PillarsPerRay = CustomConfigFile.Bind<int>("Circle Snap", "Pillars Per Ray", 1,
                "This determines how many pillars are dropped in each of circle snapfreeze's 'rays'. " +
                "6 pillars behaves the same as vanilla. Minimum of 1 pillar per ray.");
            PillarsPerRay.Value = Mathf.Max(PillarsPerRay.Value, 1);

            RayRotationOffset = CustomConfigFile.Bind<int>("Clapfreeze", "Ray Rotation Offset", -90,
                "This determines the rotation offset for Snapfreeze 'rays'. " +
                "This wont have a significant effect on the performance of Snapfreeze unless youre using very few rays. " +
                "Every 360 degrees loops back around to 0, you know the drill. " +
                "-90 fires the first ray towards you - behaving the same as ArtificerExtended's long lost Clapfreeze skill.");

            UseClapfreezeAssets = CustomConfigFile.Bind<bool>("Clapfreeze", "Use Clapfreeze Assets", false,
                "Enable cosmetic changes to make the Snapfreeze skill use the Clapfreeze assets from ArtificerExtended.");
        }

        private void InitializeCustomWalker()
        {
            // Instantiate a clone of the original walker.
            // We could just modify the original directly, but it introduces compatability issues if anyone else wants to build off of it.
            CircleSnapWalkerPrefab = Resources.Load<GameObject>("prefabs/projectiles/MageIcewallWalkerProjectile").InstantiateClone("CircleSnapWalker", true);

            // First, we have to do a little math in order to decipher our config values.
            // I would've liked to just config everything we wanted directly, but that comes at the cost of user intuition. Not the end of the world.
            float lifetime = CircleMaxDeployTime.Value;
            float velocity = CircleMaxRadius.Value / lifetime; //essentially meters per second
            float dropInterval = lifetime / (PillarsPerRay.Value - 0.5f) - 0.01f; 
            // The walkers are designed to halve the drop interval before the first pillar so the gap between the first pillars on different rays is the same as other pillars.
            // We need to accomodate for this with the drop interval, otherwise circle snap wont behave as expected.

            // We need a few key components from the projectile. I'm looking at the prefab data, so I know everything the projectile has, and what I need to change.
            // First is the ProjectileCharacterController. This is a component that is not used very often, instead most projectiles use a simple ProjectileController.
            // We aren't changing that though.
            ProjectileCharacterController projectileController = CircleSnapWalkerPrefab.GetComponent<ProjectileCharacterController>();
            if (projectileController != null) // Always do null checks when getting components, even if it feels silly.
            {
                projectileController.lifetime = lifetime;
                projectileController.velocity = velocity;
            }
            else
            {
                Log.LogError(nameof(InitializeCustomWalker) + " tried to find the ProjectileCharacterController from the walker projectile and failed.");
            }

            // Next is the ProjectileMageFirewallWalkerController. This is another unique component, however this time it's for an obvious reason.
            ProjectileMageFirewallWalkerController walkerController = CircleSnapWalkerPrefab.GetComponent<ProjectileMageFirewallWalkerController>();
            if(walkerController != null) 
            {
                walkerController.dropInterval = dropInterval;
            }
            else
            {
                Log.LogError(nameof(InitializeCustomWalker) + " tried to find the ProjectileMageFirewallWalkerController from the walker projectile and failed.");
            }

            // Lastly, whenever we create a piece of content we need to add it to our ContentPack for RoR2 to recognize it.
            // This also helps give us an idea of what needs to be synced in order for networking to happen successfully.
            ContentPacks.projectilePrefabs.Add(CircleSnapWalkerPrefab);
        }

        private void CircleTheSnapfreeze()
        {
            // Usually I would recommend loading the character's SkillLocator so that we would have access to all of their skills, however,
            // since we are only editing the one skill then it's way easier to simply load the skilldef directly.
            SkillDef snapfreeze = Resources.Load<SkillDef>("skilldefs/magebody/magebodywall");

            if (snapfreeze != null) // Dont forget the null check!
            {
                snapfreeze.activationState = new SerializableEntityStateType(typeof(States.PrepCircleWall));

                // Dont forget to add the entity state to the content pack!
                ContentPacks.entityStates.Add(typeof(States.PrepCircleWall));

                // Lastly, let's change the description of Snapfreeze to match our changes.
                // I am creating a unique description token to override snapfreeze's description, instead of using LanguageAPI to adjust the token directly.
                // This way, if anyone else changed the snapfreeze token, this takes precedent.
                string customSnapfreezeToken = "MAGE_CIRCLESNAP_ICE_DESCRIPTION";
                snapfreeze.skillDescriptionToken = customSnapfreezeToken;
                // You can put the $ before a string so that you can easily format variables into it just by inserting a curly bracket.
                LanguageAPI.Add(customSnapfreezeToken, $"<style=cIsUtility>Freezing</style>. Create a barrier that hurts enemies for " +
                    $"up to <style=cIsDamage>{PillarsPerRay.Value * TotalRays.Value}x100% damage</style>.");

                if (UseClapfreezeAssets.Value == true)
                {
                    // I would always recommend using an AssetBundle to load resources, but it's not worth explaining that process for a small mod with one asset.
                    snapfreeze.icon = iconBundle.LoadAsset<Sprite>(iconsPath + "clapfreeze.png");
                    LanguageAPI.Add("MAGE_UTILITY_ICE_NAME", $"Clapfreeze");
                }
            }
            else
            {
                Log.LogError(nameof(CircleTheSnapfreeze) + " tried to load the Snapfreeze SkillDef and failed.");
            }
        }
    }
}