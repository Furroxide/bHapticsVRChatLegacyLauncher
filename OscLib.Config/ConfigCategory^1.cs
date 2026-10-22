using System;
using Tomlet;
using Tomlet.Models;
using System.Reflection;

namespace OscLib.Config
{
    public class ConfigCategory<T> : ConfigCategory where T : ConfigCategoryValue
    {
        public T Value;

        public ConfigCategory(string name) : base(name) => LoadDefaults();

        internal override void LoadDefaults() => Value = (T)Activator.CreateInstance(typeof(T));
        internal override Action PrepareLoad(TomlValue tomlValue)
        {
            // Tomlet 3.1.3 narrows Int64 TOML numbers to Int32 without overflow checks.
            // Reject those values before conversion, rather than wrapping and then clamping.
            if (tomlValue is TomlTable table)
                foreach (FieldInfo field in typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public))
                    if (field.FieldType == typeof(int)
                        && table.Entries.TryGetValue(field.Name, out TomlValue member)
                        && member is TomlLong integer
                        && (integer.Value < int.MinValue || integer.Value > int.MaxValue))
                        throw new OverflowException($"Setting '{Name}.{field.Name}' is outside the supported integer range.");

            T value = tomlValue == null
                ? (T)Activator.CreateInstance(typeof(T))
                : (T)TomletMain.To(typeof(T), tomlValue);
            if (value == null)
                throw new FormatException($"Category '{Name}' has no usable value.");
            value.Clamp();
            return () => Value = value;
        }

        internal override TomlValue Save()
        {
            if (Value == null)
                LoadDefaults();
            Value?.Clamp();
            return TomletMain.ValueFrom(typeof(T), Value);
        }
    }
}
