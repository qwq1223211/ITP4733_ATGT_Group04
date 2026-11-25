using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Local.UnityAgent.Runtime
{
    [Serializable]
    public class QuestAssetEvent : UnityEvent<QuestAsset> { }

    /// <summary>进行中的任务。目标全部完成后移出并通知。</summary>
    public class QuestLog : MonoBehaviour
    {
        public List<QuestAsset> active = new List<QuestAsset>();
        public QuestAssetEvent onQuestStarted = new QuestAssetEvent();
        public QuestAssetEvent onQuestCompleted = new QuestAssetEvent();

        readonly Dictionary<string, HashSet<string>> _done = new Dictionary<string, HashSet<string>>();

        public void StartQuest(QuestAsset quest)
        {
            if (quest == null) return;
            if (active == null) active = new List<QuestAsset>();
            if (IsActive(quest)) return;
            active.Add(quest);
            if (onQuestStarted != null) onQuestStarted.Invoke(quest);
        }

        public bool IsActive(QuestAsset quest)
        {
            if (quest == null || active == null) return false;
            for (var i = 0; i < active.Count; i++)
            {
                if (active[i] == quest) return true;
            }
            return false;
        }

        public void CompleteObjective(string objective)
        {
            if (string.IsNullOrEmpty(objective) || active == null) return;
            var finished = new List<QuestAsset>();
            for (var i = 0; i < active.Count; i++)
            {
                var quest = active[i];
                if (quest == null || quest.objectives == null) continue;
                if (!Lists(quest, objective)) continue;
                var key = string.IsNullOrEmpty(quest.id) ? quest.name : quest.id;
                if (!_done.TryGetValue(key, out var set))
                {
                    set = new HashSet<string>();
                    _done[key] = set;
                }
                set.Add(objective);
                if (AllDone(quest, set)) finished.Add(quest);
            }
            for (var i = 0; i < finished.Count; i++)
            {
                active.Remove(finished[i]);
                if (onQuestCompleted != null) onQuestCompleted.Invoke(finished[i]);
            }
        }

        static bool Lists(QuestAsset quest, string objective)
        {
            for (var i = 0; i < quest.objectives.Length; i++)
            {
                if (quest.objectives[i] == objective) return true;
            }
            return false;
        }

        static bool AllDone(QuestAsset quest, HashSet<string> set)
        {
            var any = false;
            for (var i = 0; i < quest.objectives.Length; i++)
            {
                var item = quest.objectives[i];
                if (string.IsNullOrEmpty(item)) continue;
                any = true;
                if (!set.Contains(item)) return false;
            }
            return any;
        }
    }
}
