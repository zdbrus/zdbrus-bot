namespace Il2Cpp;

public enum HeroCode { Talia, Burt, Boris }
public enum GearSlot { Weapon, Chest, Boots, Wrist, Shoulder, Belt, Ring, Relic }
public enum GearTier { Tier1 = 1, Tier2, Tier3 }
public enum Rarity { None, Common, Uncommon, Rare }
public struct ObscuredInt
{
    public int Value;
    public int GetDecrypted() => Value;
    public static implicit operator ObscuredInt(int value) => new() { Value = value };
}
public struct ObscuredLong
{
    public long Value;
    public long GetDecrypted() => Value;
    public static implicit operator ObscuredLong(long value) => new() { Value = value };
}
public struct ObscuredDouble
{
    public double Value;
    public double GetDecrypted() => Value;
    public static implicit operator ObscuredDouble(double value) => new() { Value = value };
}
public struct ObscuredBigDouble
{
    public ObscuredDouble Mantissa;
    public ObscuredLong Exponent;
    public static implicit operator ObscuredBigDouble(double value) => new() { Mantissa = value, Exponent = 0 };
    public static explicit operator double(ObscuredBigDouble value) =>
        value.Mantissa.GetDecrypted() * Math.Pow(10, value.Exponent.GetDecrypted());
}
public static class GearEffects
{
    public static Dictionary<ObscuredInt, ObscuredBigDouble> enchantEffects = new()
    {
        [(ObscuredInt)0] = 1, [(ObscuredInt)1] = 2, [(ObscuredInt)2] = 4,
        [(ObscuredInt)3] = 8, [(ObscuredInt)4] = 15, [(ObscuredInt)5] = 30,
        [(ObscuredInt)6] = 60,
    };
    public static Dictionary<GearSlot, Dictionary<Rarity, ObscuredBigDouble>> allGearRarityEffects =
        Enum.GetValues<GearSlot>().ToDictionary(slot => slot, slot => new Dictionary<Rarity, ObscuredBigDouble>
        {
            [Rarity.Common] = slot == GearSlot.Relic ? 8.5 : slot == GearSlot.Ring ? 2.5 : (int)slot < 3 ? 4 : 3,
            [Rarity.Uncommon] = slot == GearSlot.Relic ? 23.5 : slot == GearSlot.Ring ? 5.5 : (int)slot < 3 ? 10 : 8,
            [Rarity.Rare] = slot == GearSlot.Relic ? 68.5 : slot == GearSlot.Ring ? 15 : (int)slot < 3 ? 28 : 21,
        });
}
public class BattleCharactersHolder { public TeamLogic heroesTeamLogic = new(); }
public class TeamLogic
{
    public HeroBattleLogic[] Heroes = Array.Empty<HeroBattleLogic>();
    public HeroBattleLogic[] GetHeroLogics() => Heroes;
}
public class HeroBattleLogic { public BattleHero battleHero; }
public class BattleHero
{
    public Hero hero;
    public ObscuredBigDouble DPS;
    public ObscuredBigDouble maximumHealthTotal;
    public ObscuredBigDouble armorTotal;
}
public class Currency { public double amount; }
public class CurrencyHandler { public Dictionary<CurrencyCode, Currency> currencies = new(); }
public class Hero
{
    public int code;
    public int HeroCode => code;
    public bool Unlocked = true;
    public List<GearItem> heroGearItems = new();
    public ObscuredBool IsUnlocked() => new() { Value = Unlocked };
}
public class HeroDeck { public Hero[] heroes = Array.Empty<Hero>(); }
public class HeroServer
{
    public int code;
    public HeroCode heroCode => (HeroCode)code;
    public int gearTier = 3;
    public Dictionary<GearSlot, GearItem> gearsDict = new();
    public List<GearServer> gearsRaw = new();
    public List<GearItem> heroGears = new();
}
public class GearServer { public int h, s, r, e, q; }
public class HeroServerHandler
{
    public bool hasLoaded = true;
    public Dictionary<HeroCode, HeroServer> heroesDict = new();
}
public class GearItem
{
    public HeroCode heroId;
    public GearSlot gearSlot;
    public GearTier gearTier = GearTier.Tier2;
    public Rarity itemRarity = Rarity.Common;
    public int EnchantLevel;
    public int quantity;
    public int MaxLevel = 2;
    public ObscuredDouble enchantCost = 60;
    public bool ThrowOnCostText;
    public double? EffectOverride;
    public double? PowerOverride;
    public ObscuredBigDouble effect => EffectOverride ??
        ((double)GearEffects.allGearRarityEffects[gearSlot][itemRarity] *
         (double)GearEffects.enchantEffects[(ObscuredInt)EnchantLevel] - 1);
    public ObscuredDouble power => PowerOverride ??
        ((int)gearTier == 1 ? 100 : (int)gearTier == 2 ? 150 : 250) *
        Math.Pow(3, (int)itemRarity - 1) * Math.Pow(2, EnchantLevel);
    public ObscuredBool IsMaxEnchantLvl() => new() { Value = EnchantLevel >= 20 };
    public ObscuredBool MaxEnchantLvlPerRarity() => new() { Value = EnchantLevel >= MaxLevel };
    public string GetCurrentEffectText() => ThrowOnCostText ? throw new InvalidOperationException("gear read failed") : "x2";
    public string NextEnchantEffectText() => "x4";
}
public class EnchantGearItemInteraction
{
    public GearSlot slot;
    public GearItem gearItem;
    public UnityEngine.UI.Button enchantGearItemButton;
}
public class HOHEnchantSubmenuViewController
{
    public UnityEngine.UI.Button CategoryGearButton;
    public UnityEngine.GameObject CategoryGearObj;
    public EnchantGearItemInteraction[] GearItems = Array.Empty<EnchantGearItemInteraction>();
}
public class HeroInteractionSquare { public Hero hero; }
public class HallOfHeroesMech
{
    public HeroServer heroServerSelected;
    public bool heroSelectionLoading;
    public List<HeroInteractionSquare> heroInteractions = new();
    public Action<int> OnSelect;
    public void SelectHero(int index, bool onOpen) => OnSelect?.Invoke(heroInteractions[index].hero.code);
    public void GoForthHero() => OnSelect?.Invoke((heroServerSelected.code + 1) % heroInteractions.Count);
}
