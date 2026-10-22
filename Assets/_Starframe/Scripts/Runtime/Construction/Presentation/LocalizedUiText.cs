// @role Bind localized table entries and formatting arguments to UI labels.
// @calls L10n

using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;

namespace Starframe.Presentation
{
    [DisallowMultipleComponent]
    public sealed class LocalizedUiText : MonoBehaviour
    {
        TMP_Text target;
        LocalizeStringEvent localizer;

        public void Configure(TMP_Text text, string key, object[] args)
        {
            target = text;
            if (!localizer) localizer = GetComponent<LocalizeStringEvent>();
            if (!localizer) localizer = gameObject.AddComponent<LocalizeStringEvent>();
            localizer.OnUpdateString.RemoveListener(ApplyText);
            localizer.OnUpdateString.AddListener(ApplyText);
            localizer.StringReference = new LocalizedString(L10n.StringTable, key) { Arguments = args };
            localizer.enabled = true;
            localizer.RefreshString();
        }

        public void Clear()
        {
            if (!localizer) return;
            localizer.enabled = false;
            localizer.OnUpdateString.RemoveListener(ApplyText);
        }

        public void ApplyText(string value)
        {
            if (!target) return;
            target.text = value;
            // Formatted labels may contain a user blueprint name from another script.
            L10n.ApplyUserContentFont(target);
            if (target.transform.parent is RectTransform parent)
                UnityEngine.UI.LayoutRebuilder.MarkLayoutForRebuild(parent);
        }
    }
}
