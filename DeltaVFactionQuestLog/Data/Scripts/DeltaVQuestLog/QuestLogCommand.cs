using ProtoBuf;

namespace Invalid.DeltaVQuestLog
{
    [ProtoContract]
    internal class QuestLogCommand
    {
        [ProtoMember(1)] public string Command;
        [ProtoMember(2)] public string Arguments;

        public QuestLogCommand(string messageText, out bool isValid)
        {
            string[] contents = messageText.Split(' ');
            if (contents.Length < 2)
            {
                isValid = false;
                return;
            }

            isValid = true;

            Command = contents[1].ToLower();
            Arguments = string.Join(" ", contents, 2, contents.Length - 2);
        }

        private QuestLogCommand()
        {
        }
    }
}
