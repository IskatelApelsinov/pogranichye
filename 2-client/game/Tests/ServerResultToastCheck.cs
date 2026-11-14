using NUnit.Framework;
using Pogranichye.Client.Presentation;
using UnityEngine;

namespace Pogranichye.Client.Tests
{
    public sealed class ServerResultToastCheck
    {
        [Test]
        public void Width_IsWithinReadableBand()
        {
            var w = ServerResultToast.WidthForScreen(1920f);
            Assert.GreaterOrEqual(w, ServerResultToast.MinWidth);
            Assert.LessOrEqual(w, 1920f * ServerResultToast.MaxWidthFraction + 1f);
        }

        [Test]
        public void Height_GrowsWithLongReason()
        {
            var shortH = ServerResultToast.HeightForText("ок", 400f);
            var longH = ServerResultToast.HeightForText(
                "надо подойти к стойке корчмы — корчмаря сейчас нет за стойкой, зайди днём",
                400f);
            Assert.Greater(longH, shortH);
        }

        [Test]
        public void DisplayText_DoesNotTruncate()
        {
            const string reason = "мало пива в корчме — приходи после варки";
            Assert.AreEqual(reason, ServerResultToast.DisplayText("  " + reason + "  "));
        }

        [Test]
        public void Rect_StaysOnScreen()
        {
            var r = ServerResultToast.RectForScreen(1280f, 720f,
                "отказ: надо подойти к стойке корчмы");
            Assert.GreaterOrEqual(r.x, 0f);
            Assert.LessOrEqual(r.xMax, 1280f);
            Assert.LessOrEqual(r.yMax, 720f);
        }
    }
}
