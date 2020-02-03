using System.Linq;
using Content.Server.GameObjects.Components.Interactable.Tools;
using Content.Server.GameObjects.Components.Stack;
using Content.Server.GameObjects.EntitySystems;
using Robust.Server.Interfaces.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.GameObjects.Components.Transform;
using Robust.Shared.IoC;
using Robust.Shared.ViewVariables;

namespace Content.Server.GameObjects.Components.Power
{
    /// <summary>
    /// Component to transfer power to nearby components, can create powernets and connect to nodes
    /// </summary>
    [RegisterComponent]
    public class PowerTransferComponent : Component, IAttackBy
    {
        public override string Name => "PowerTransfer";

        /// <summary>
        /// The powernet this component is connected to
        /// </summary>
        [ViewVariables]
        public Powernet Parent { get; set; }

        [ViewVariables]
        public bool Regenerating { get; set; } = false;

        protected override void Startup()
        {
            base.Startup();

            if (Parent == null)
            {
                SpreadPowernet();
            }
        }

        public override void OnRemove()
        {
            DisconnectFromPowernet();

            base.OnRemove();
        }

        /// <summary>
        /// Searches for local powernets to connect to, otherwise creates its own, and spreads powernet to nearby entities
        /// </summary>
        public void SpreadPowernet() {
            var sgc = Owner.GetComponent<SnapGridComponent>();
            var ptcs = sgc.GetCardinalNeighborCells()
                .SelectMany(x => x.GetLocal()).Distinct()
                .Select(x => x.TryGetComponent<PowerTransferComponent>(out var c) ? c : null)
                .Where(x => x != null).Distinct()
                .ToArray();
            /*
            var ptcs = entMan.GetEntitiesInRange(Owner, 1.1f)
                .Select(x => x.TryGetComponent<PowerTransferComponent>(out var c) ? c : null)
                .Where(ptc => ptc != null && ptc.Owner.Uid != uid)
                .Distinct()
                .ToArray();
            */
            var entMan = IoCManager.Resolve<IServerEntityManager>();
            //var position = Owner.GetComponent<ITransformComponent>().WorldPosition;
            //var cells = sgc.GetCardinalNeighborCells().ToArray();
            var uid = Owner.Uid;

            //we have no parent so lets find a partner we can join his powernet
            if (Parent == null || Regenerating)
            {
                foreach (var ptc in ptcs)
                {
                    if (ptc.CanConnectTo())
                    {
                        ConnectToPowernet(ptc.Parent);
                        break;
                    }
                }

                //we couldn't find a partner so none must have spread yet, lets make our own powernet to spread
                if (Parent == null || Regenerating)
                {
                    ConnectToPowernet(new Powernet());
                }
            }

            //Find nodes intersecting us and if not already assigned to a powernet assign them to us
            var nodes = entMan.GetEntitiesIntersecting(Owner)
                .Select(x => x.TryGetComponent<PowerNodeComponent>(out var pnc) ? pnc : null)
                .Where(x => x != null)
                .ToArray();

            foreach (var node in nodes)
            {
                if (node.Parent == null)
                {
                    node.ConnectToPowernet(Parent);
                }
                else if (node.Parent.Dirty)
                {
                    node.RegeneratePowernet(Parent);
                }
            }

            //spread powernet to nearby wires which haven't got one yet, and tell them to spread as well
            foreach (var ptc in ptcs)
            {
                if (ptc.Parent == null || Regenerating)
                {
                    ptc.ConnectToPowernet(Parent);
                    ptc.SpreadPowernet();
                }
                else if (ptc.Parent != Parent && !ptc.Parent.Dirty)
                {
                    Parent.MergePowernets(ptc.Parent);
                }
            }
        }

        /// <summary>
        /// Called when connecting to a new powernet, either on creation or on regeneration
        /// </summary>
        /// <param name="toconnect"></param>
        public void ConnectToPowernet(Powernet toconnect)
        {
            Parent = toconnect;
            Parent.WireList.Add(this);
            Regenerating = false;
        }

        /// <summary>
        /// Called when we are removed and telling the powernet that it is now dirty and must regenerate
        /// </summary>
        public void DisconnectFromPowernet()
        {
            if (Parent == null)
                return;
            Parent.WireList.Remove(this);
            Parent.Dirty = true;
            Parent = null;
        }


        public bool CanConnectTo()
        {
            return Parent != null && Parent.Dirty == false && !Regenerating;
        }

        public bool AttackBy(AttackByEventArgs eventArgs)
        {
            if (eventArgs.AttackWith.TryGetComponent(out WirecutterComponent wirecutter))
            {
                Owner.Delete();
                var droppedEnt = Owner.EntityManager.SpawnEntity("CableStack", eventArgs.ClickLocation);

                if (droppedEnt.TryGetComponent<StackComponent>(out var stackComp))
                    stackComp.Count = 1;

                return true;
            }
            return false;
        }
    }
}
