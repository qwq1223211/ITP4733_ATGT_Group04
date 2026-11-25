using UnityEngine;

namespace Local.UnityAgent.Runtime
{
    /// <summary>一条任务：编号、标题、说明和目标。</summary>
    public class QuestAsset : ScriptableObject
    {
        public string id;
        public string title;
        public string description;
        public string[] objectives = new string[0];
    }
}
