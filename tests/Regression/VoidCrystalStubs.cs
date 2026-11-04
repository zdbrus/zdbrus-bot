namespace Il2Cpp;

public enum HeroCode { Talia, Burt, Boris }
public enum GearSlot { Weapon, Chest, Boots, Wrist, Shoulder, Belt, Ring, Relic }
public enum GearTier { Tier1 = 1, Tier2, Tier3 }
public enum Rarity { None, Common, Uncommon, Rare }
public struct ObscuredDouble
{
    public double Value;
    public double GetDecrypted() => Value;
    public static implicit operator ObscuredDouble(double value) => new() { Value = value };
}
public class Currency { public double amount; }
public class CurrencyHandler { public Dictionary<CurrencyCode, Currency> currencies = new(); }
public class Hero
{
    public int code;
    public int HeroCode => code;
    public bool Unlocked = true;
    public ObscuredBool IsUnlocked() => new() { Value = Unlocked };
}
public class HeroDeck { public Hero[] heroes = Array.Empty<Hero>(); }
public class HeroServer
{
    public int code;
    public HeroCode heroCode => (HeroCode)code;
    public int gearTier = 3;
    public Dictionary<GearSlot, GearItem> gearsDict = new();
}
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
    public int MaxLevel = 2;
    public ObscuredDouble enchantCost = 60;
    public bool ThrowOnCostText;
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
