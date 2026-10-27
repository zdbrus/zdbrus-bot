// Compiler marker attributes that newer C# compilers emit but .NET 6 (the runtime MelonLoader uses) does not ship
// publicly. Declaring them here makes the compiler embed its own copies instead of pointing at a runtime that
// lacks them, so the mod loads on .NET 6 no matter which SDK built it.

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Module, AllowMultiple = false, Inherited = false)]
    internal sealed class RefSafetyRulesAttribute : Attribute
    {
        public readonly int Version;
        public RefSafetyRulesAttribute(int version) => Version = version;
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Delegate | AttributeTargets.Interface | AttributeTargets.Method | AttributeTargets.Struct,
        AllowMultiple = false, Inherited = false)]
    internal sealed class NullableContextAttribute : Attribute
    {
        public readonly byte Flag;
        public NullableContextAttribute(byte flag) => Flag = flag;
    }
}
