using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DrivingSchool.Presentation.UI
{
    /// <summary>Ссылки карточки нарушения (шаблон собирает UIBuilder): внешний слой держит место в столбце, тело выезжает.</summary>
    public sealed class DriveHudCard : MonoBehaviour
    {
        public CanvasGroup group;
        public RectTransform body;
        public Image bar, icon;
        public TMP_Text title, reference, advice;
    }
}
