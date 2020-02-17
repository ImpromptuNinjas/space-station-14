using System;
using Lidgren.Network;
using Robust.Shared.GameObjects;
using Robust.Shared.Interfaces.Network;
using Robust.Shared.Network;
using Robust.Shared.Network.Messages;
using Robust.Shared.Serialization;

namespace Content.Shared.Chat
{

    [Serializable, NetSerializable]
    public struct ChatMessageState
    {

        public string Message { get; set; }

        public string MessageWrap { get; set; }

    }

    /// <summary>
    ///     Sent from server to client to notify the client about a new chat message.
    /// </summary>
    public sealed class MsgChatMessage : NetMessageCompressed
    {

        #region REQUIRED

        public const MsgGroups GROUP = MsgGroups.Command;

        public const string NAME = nameof(MsgChatMessage);

        public MsgChatMessage(INetChannel channel) : base(NAME, GROUP) { }

        #endregion

        /// <summary>
        ///     The channel the message is on. This can also change whether certain params are used.
        /// </summary>
        public ChatChannel Channel { get; set; }

        private ChatMessageState state;

        /// <summary>
        ///     The actual message contents.
        /// </summary>
        public string Message
        {
            get => state.Message;
            set => state.Message = value;
        }

        /// <summary>
        ///     What to "wrap" the message contents with. Example is stuff like 'Joe says: "{0}"'
        /// </summary>
        public string MessageWrap
        {
            get => state.MessageWrap;
            set => state.MessageWrap = value;
        }

        /// <summary>
        ///     The sending entity.
        ///     Only applies to <see cref="ChatChannel.Local"/> and <see cref="ChatChannel.Emotes"/>.
        /// </summary>
        public EntityUid SenderEntity { get; set; }

        public override void ReadFromBuffer(NetIncomingMessage buffer)
        {
            Channel = (ChatChannel) buffer.ReadByte();

            switch (Channel)
            {
                case ChatChannel.Local:
                case ChatChannel.Emotes:
                    SenderEntity = buffer.ReadEntityUid();
                    break;
            }

            DeserializeFromBuffer(buffer, out state, out _);
        }

        public override void WriteToBuffer(NetOutgoingMessage buffer)
        {
            buffer.Write((byte) Channel);

            switch (Channel)
            {
                case ChatChannel.Local:
                case ChatChannel.Emotes:
                    buffer.Write(SenderEntity);
                    break;
            }

            SerializeToBuffer(buffer, state);
        }

    }

}
