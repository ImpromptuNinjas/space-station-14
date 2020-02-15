using System;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared.GameObjects.Components
{
    public abstract class SharedStackComponent : Component
    {
        public sealed override string Name => "Stack";
        public sealed override uint? NetID => ContentNetIDs.STACK;

        [Serializable, NetSerializable]
        protected sealed class StackComponentState : ComponentState
        {
            public override uint NetID => ContentNetIDs.STACK;
            public int Count { get; }
            public int MaxCount { get; }

            public StackComponentState(int count, int maxCount)
            {
                Count = count;
                MaxCount = maxCount;
            }
        }
    }
}
