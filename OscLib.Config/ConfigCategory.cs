using Tomlet.Models;

namespace OscLib.Config
{
    public abstract class ConfigCategory
    {
        public string Name;

        internal ConfigCategory(string name)
            => Name = name;

        // Validate and clamp every category before publishing any part of a new file.
        internal abstract System.Action PrepareLoad(TomlValue tomlValue);
        internal abstract void LoadDefaults();
        internal abstract TomlValue Save();
    }
}
