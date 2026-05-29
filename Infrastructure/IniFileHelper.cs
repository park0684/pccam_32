using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace pccam_32.Infrastructure
{
    /// <summary>
    /// Windows INI 파일 읽기/쓰기 Helper.
    /// 
    /// Windows API의 GetPrivateProfileString / WritePrivateProfileString을 사용한다.
    /// Windows 7 32bit 환경에서도 사용할 수 있다.
    /// </summary>
    public class IniFileHelper
    {
        private readonly string _filePath;

        public IniFileHelper(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("INI 파일 경로가 비어 있습니다.", "filePath");

            _filePath = filePath;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetPrivateProfileString(
            string section,
            string key,
            string defaultValue,
            StringBuilder retVal,
            int size,
            string filePath);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern long WritePrivateProfileString(
            string section,
            string key,
            string value,
            string filePath);

        /// <summary>
        /// INI 파일에서 문자열 값을 읽는다.
        /// 값이 없으면 defaultValue를 반환한다.
        /// </summary>
        public string ReadString(string section, string key, string defaultValue = "")
        {
            StringBuilder buffer = new StringBuilder(2048);

            GetPrivateProfileString(
                section,
                key,
                defaultValue,
                buffer,
                buffer.Capacity,
                _filePath);

            return buffer.ToString();
        }

        /// <summary>
        /// INI 파일에 문자열 값을 저장한다.
        /// </summary>
        public void WriteString(string section, string key, string value)
        {
            EnsureDirectory();

            WritePrivateProfileString(
                section,
                key,
                value ?? "",
                _filePath);
        }

        /// <summary>
        /// INI 파일에서 int 값을 읽는다.
        /// 숫자로 변환할 수 없으면 defaultValue를 반환한다.
        /// </summary>
        public int ReadInt(string section, string key, int defaultValue = 0)
        {
            string value = ReadString(section, key, defaultValue.ToString());

            int result;
            if (int.TryParse(value, out result))
                return result;

            return defaultValue;
        }

        /// <summary>
        /// INI 파일에 int 값을 저장한다.
        /// </summary>
        public void WriteInt(string section, string key, int value)
        {
            WriteString(section, key, value.ToString());
        }

        /// <summary>
        /// INI 파일에서 bool 값을 읽는다.
        /// True, true, 1, Y, y 값을 true로 처리한다.
        /// </summary>
        public bool ReadBool(string section, string key, bool defaultValue = false)
        {
            string defaultText = defaultValue ? "True" : "False";
            string value = ReadString(section, key, defaultText);

            if (string.Equals(value, "True", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(value, "Y", StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(value, "Yes", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        /// <summary>
        /// INI 파일에 bool 값을 저장한다.
        /// </summary>
        public void WriteBool(string section, string key, bool value)
        {
            WriteString(section, key, value ? "True" : "False");
        }

        /// <summary>
        /// INI 파일 존재 여부.
        /// </summary>
        public bool Exists()
        {
            return File.Exists(_filePath);
        }

        /// <summary>
        /// INI 파일이 저장될 폴더를 생성한다.
        /// </summary>
        private void EnsureDirectory()
        {
            string directory = Path.GetDirectoryName(_filePath);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);
        }
    }
}