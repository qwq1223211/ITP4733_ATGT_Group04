using UnityEngine;

namespace Local.UnityAgent.Runtime
{
    /// <summary>一段对话的台词列表。</summary>
    public class DialogueAsset : ScriptableObject
    {
        public DialogueLine[] lines = new DialogueLine[0];
    }
}
