using OscLib.Config;
using Tomlet.Attributes;

namespace bHapticsOSC
{
    public class VRChatConfig : ConfigFile
    {
        public ConfigCategory<Reactivity> reactivity;
        public ConfigCategory<Punch> punch;
        public ConfigCategory<AvatarOSCConfigReset> avatarOSCConfigReset;

        public VRChatConfig(string filepath) : base(filepath)
        {
            Categories.AddRange(new ConfigCategory[]
            {
                reactivity = new ConfigCategory<Reactivity>(nameof(Reactivity)),
                punch = new ConfigCategory<Punch>(nameof(Punch)),
                avatarOSCConfigReset = new ConfigCategory<AvatarOSCConfigReset>(nameof(avatarOSCConfigReset))
            });
        }

        [TomlDoNotInlineObject]
        public class Reactivity : ConfigCategoryValue
        {
            [TomlPrecedingComment("If the Devices should React while AFK.")]
            public bool AFK = true;
            [TomlPrecedingComment("If the Devices should React while in a Station.")]
            public bool InStation = true;
            [TomlPrecedingComment("If the Devices should React while Seated in a Station.")]
            public bool Seated = true;
        }

        [TomlDoNotInlineObject]
        public class Punch : ConfigCategoryValue
        {
            [TomlPrecedingComment("If Hand/Foot impact events should create punch haptics on the vest.")]
            public bool Enabled = true;

            [TomlPrecedingComment("If punch events should radiate to nearby vest motors.")]
            public bool Ripple = true;

            [TomlPrecedingComment("Punch intensity multiplier percentage. (0 - 200)")]
            public int Strength = 100;

            [TomlPrecedingComment("Punch duration multiplier percentage. (0 - 200)")]
            public int Duration = 100;

            [TomlPrecedingComment("Base motor intensity for a normal punch velocity band. (0 - 255)")]
            public int LightIntensity = 140;

            [TomlPrecedingComment("Base motor intensity for a hard punch velocity band. (0 - 255)")]
            public int HardIntensity = 220;

            [TomlPrecedingComment("Base duration in milliseconds for a normal punch velocity band. (100 - 1000)")]
            public int LightDurationMs = 260;

            [TomlPrecedingComment("Base duration in milliseconds for a hard punch velocity band. (100 - 1000)")]
            public int HardDurationMs = 520;

            [TomlPrecedingComment("Milliseconds between each ripple ring. (0 - 250)")]
            public int RippleDelayMs = 80;

            public override void Clamp()
            {
                Strength = OscLib.Utils.Extensions.Clamp(Strength, 0, 200);
                Duration = OscLib.Utils.Extensions.Clamp(Duration, 0, 200);
                LightIntensity = OscLib.Utils.Extensions.Clamp(LightIntensity, 0, 255);
                HardIntensity = OscLib.Utils.Extensions.Clamp(HardIntensity, 0, 255);
                LightDurationMs = OscLib.Utils.Extensions.Clamp(LightDurationMs, 100, 1000);
                HardDurationMs = OscLib.Utils.Extensions.Clamp(HardDurationMs, 100, 1000);
                RippleDelayMs = OscLib.Utils.Extensions.Clamp(RippleDelayMs, 0, 250);
            }
        }

        [TomlDoNotInlineObject]
        public class AvatarOSCConfigReset : ConfigCategoryValue
        {
            [TomlPrecedingComment("If the Application should Automatically Reset the Avatar's OSC Config on Change.")]
            public bool Enabled = true;
        }
    }
}
