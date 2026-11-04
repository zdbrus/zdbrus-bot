namespace Il2Cpp;

public class MeteoriteResearch
{
    private static int _next;
    public IntPtr Pointer { get; } = (IntPtr)Interlocked.Increment(ref _next);
    public int index, researchTree, meteoriteResearchType, level;
    public string name;
    public int maxLevel = 20;
    public ObscuredDouble levelUpCost;
    public bool Unlocked = true;
    public bool IsUnlocked() => Unlocked;
    public bool IsMaxLevel() => level >= maxLevel;
}

public class MeteoriteResearchInteraction : UnityEngine.Component
{
    public MeteoriteResearch meteoriteResearch;
}

public class MeteoriteResearchPreviewPopup
{
    public enum ResearchQuantity { ResearchX1, ResearchX5, ResearchX10 }
    public MeteoriteResearch meteoriteResearch;
    public int currentResearchQuantity = 1;
    public ResearchQuantity currentResearchQuantityEnum;
}
