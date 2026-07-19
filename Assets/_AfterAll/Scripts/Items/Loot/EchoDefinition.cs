using AfterAll.Items;
using UnityEngine;

namespace AfterAll.Items.Loot
{
    /// <summary>Physical size class for stash/pockets capacity rules (S3).</summary>
    public enum EchoSizeClass
    {
        Small,
        Bulky,
    }

    /// <summary>
    /// Loot-specific metadata for an Echo, paired with the ItemDefinition that drives pickup/prompt.
    /// ItemDefinition is sealed, so this composes rather than extends it.
    /// </summary>
    [CreateAssetMenu(menuName = "AfterAll/Echo Definition", fileName = "NewEcho")]
    public sealed class EchoDefinition : ScriptableObject
    {
        [SerializeField] private ItemDefinition _item;
        [SerializeField] private int _value = 10;
        [SerializeField] private EchoSizeClass _sizeClass = EchoSizeClass.Small;

        public ItemDefinition Item => _item;
        public int Value => _value;
        public EchoSizeClass SizeClass => _sizeClass;
    }
}
