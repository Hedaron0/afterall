using System.Collections.Generic;
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
        // ItemDefinition can't point back to loot metadata (sealed, and most items aren't loot),
        // so each EchoDefinition registers itself here on load — lets EchoPocket resolve
        // value/size class from the ItemDefinition a WorldItem/IItemReceiver actually carries.
        private static readonly Dictionary<ItemDefinition, EchoDefinition> _registry = new();

        [SerializeField] private ItemDefinition _item;
        [SerializeField] private int _value = 10;
        [SerializeField] private EchoSizeClass _sizeClass = EchoSizeClass.Small;

        public ItemDefinition Item => _item;
        public int Value => _value;
        public EchoSizeClass SizeClass => _sizeClass;

        private void OnEnable()
        {
            if (_item != null)
                _registry[_item] = this;
        }

        public static bool TryGetFor(ItemDefinition item, out EchoDefinition definition)
        {
            if (item == null)
            {
                definition = null;
                return false;
            }

            return _registry.TryGetValue(item, out definition);
        }

        /// <summary>
        /// Explicit load-and-register for every EchoDefinition asset, called once at run start
        /// (see RunDirector.Awake). OnEnable alone isn't reliable: nothing in the scene actually
        /// references an EchoDefinition asset (ItemDefinition can't point back to it), so Unity
        /// has no reason to load it into memory before something asks for it by name — this call
        /// forces the load (and therefore OnEnable) for every asset the caller passes in.
        /// </summary>
        public static void RegisterAll(IEnumerable<EchoDefinition> definitions)
        {
            if (definitions == null)
                return;

            foreach (EchoDefinition def in definitions)
            {
                if (def != null && def.Item != null)
                    _registry[def.Item] = def;
            }
        }
    }
}
