using System.Text;
using TMPro;
using UnityEngine;

namespace TiltEm
{
    /// <summary>
    /// Every body's tilt and rotation state, as one table.
    /// </summary>
    //A table rather than a row per value: the whole point is comparing bodies against each
    //other, and a tilted moon is only wrong relative to its parent.
    internal class BodiesScreen : MonoBehaviour
    {
        /// <summary>Where each column starts, as a percentage of the table's width.</summary>
        //TMP positions the next glyph outright, so columns line up whatever the glyph widths
        //are. Padding to a character count cannot do that in a proportional font, and forcing
        //one with mspace squeezed the glyphs into cells narrower than themselves.
        private static readonly int[] Columns = { 0, 22, 34, 46, 58, 79 };

        private static readonly string[] Headings =
            { "Body", "Tilt", "Rot", "Direct", "Transform", "Body frame" };

        private static readonly StringBuilder Builder = new StringBuilder();

        [SerializeField]
        private TextMeshProUGUI _table;

        /// <summary>Builds the tab once, while the prefab is still inactive.</summary>
        internal void BuildUi()
        {
            DebugUi.CreateHeader(transform, "Bodies");
            _table = DebugUi.CreateBlock(transform);
        }

        // ReSharper disable once UnusedMember.Local
        private void Update()
        {
            _table.text = BuildTable();
        }

        private static string BuildTable()
        {
            Builder.Length = 0;

            for (int i = 0; i < Headings.Length; i++)
            {
                Cell(i, Headings[i]);
            }

            for (int i = 0; i < FlightGlobals.Bodies.Count; i++)
            {
                CelestialBody body = FlightGlobals.Bodies[i];

                Builder.AppendLine();

                Cell(0, body.bodyName);
                Cell(1, TiltEm.GetTiltForDisplay(body.bodyName) + "°");
                Cell(2, body.rotationAngle.ToString("F1"));
                Cell(3, body.directRotAngle.ToString("F1"));
                Cell(4, DebugFormat.EulerCompact(body.transform.rotation));
                Cell(5, DebugFormat.EulerCompact(body.BodyFrame.Rotation));
            }

            return Builder.ToString();
        }

        /// <summary>Starts a column at its own position, so nothing can run into the next one.</summary>
        private static void Cell(int column, string text)
        {
            Builder.Append("<pos=").Append(Columns[column]).Append("%>").Append(text);
        }
    }
}
