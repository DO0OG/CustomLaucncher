using System;
using System.Drawing;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CustomLauncher
{
    /// <summary>
    /// 임베디드 폰트 리소스를 메모리에 로드하고 UI 컨트롤에 적용하는 유틸리티 클래스
    /// </summary>
    public static class FontLibrary
    {
        private static PrivateFontCollection privateFonts = new PrivateFontCollection();
        private static Font dnfbitbitv2;

        /// <summary>
        /// 임베디드 DNFBitBitv2 폰트를 메모리에 로드합니다.
        /// 앱 시작 시 최초 1회 호출해야 합니다.
        /// </summary>
        public static void Initialize()
        {
            byte[] fontData = Properties.Resources.DNFBitBitv2;
            IntPtr fontPtr = Marshal.AllocCoTaskMem(fontData.Length);
            Marshal.Copy(fontData, 0, fontPtr, fontData.Length);
            privateFonts.AddMemoryFont(fontPtr, fontData.Length);
            Marshal.FreeCoTaskMem(fontPtr);

            FontFamily fontFamily = privateFonts.Families[0];
            dnfbitbitv2 = new Font(fontFamily, 12f, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        /// <summary>
        /// 로드된 DNFBitBitv2 폰트 인스턴스를 반환합니다.
        /// 초기화되지 않은 경우 자동으로 Initialize()를 호출합니다.
        /// </summary>
        public static Font GetFont()
        {
            if (dnfbitbitv2 == null)
                Initialize();

            return dnfbitbitv2;
        }

        /// <summary>
        /// 지정한 컨트롤 및 모든 자식 컨트롤에 DNFBitBitv2 폰트를 재귀적으로 적용합니다.
        /// </summary>
        /// <param name="parentControl">폰트를 적용할 최상위 컨트롤</param>
        public static void ApplyToControls(Control parentControl)
        {
            foreach (Control control in parentControl.Controls)
            {
                control.Font = new Font(GetFont().FontFamily, control.Font.Size, control.Font.Style);
                if (control.HasChildren)
                    ApplyToControls(control);
            }
        }
    }
}
