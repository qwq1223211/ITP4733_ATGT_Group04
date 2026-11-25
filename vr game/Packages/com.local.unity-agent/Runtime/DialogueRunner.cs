using System;
using UnityEngine;
using UnityEngine.Events;

namespace Local.UnityAgent.Runtime
{
    [Serializable]
    public class DialogueLineEvent : UnityEvent<string, string> { }

    /// <summary>逐句播放 DialogueAsset。空资产不抛错。</summary>
    public class DialogueRunner : MonoBehaviour
    {
        public DialogueAsset dialogue;
        public int index;
        public string CurrentSpeaker = "";
        public string CurrentText = "";
        public DialogueLineEvent onLine = new DialogueLineEvent();
        public UnityEvent onFinished = new UnityEvent();

        bool _finished;

        public void StartDialogue(DialogueAsset asset)
        {
            dialogue = asset;
            index = 0;
            _finished = false;
            CurrentSpeaker = "";
            CurrentText = "";
            if (!HasLines()) return;
            Show(0);
        }

        public void Next()
        {
            if (!HasLines() || _finished) return;
            var next = index + 1;
            if (next >= dialogue.lines.Length)
            {
                End();
                if (onFinished != null) onFinished.Invoke();
                return;
            }
            Show(next);
        }

        public void End()
        {
            _finished = true;
            CurrentSpeaker = "";
            CurrentText = "";
            if (HasLines()) index = dialogue.lines.Length;
        }

        void Show(int i)
        {
            index = i;
            var line = dialogue.lines[i];
            CurrentSpeaker = line != null && line.speaker != null ? line.speaker : "";
            CurrentText = line != null && line.text != null ? line.text : "";
            if (onLine != null) onLine.Invoke(CurrentSpeaker, CurrentText);
        }

        bool HasLines()
        {
            return dialogue != null && dialogue.lines != null && dialogue.lines.Length > 0;
        }
    }
}
