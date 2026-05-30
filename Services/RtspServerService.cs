using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using pccam_32.Infrastructure;
using pccam_32.Models;

namespace pccam_32.Services
{
    /// <summary>
    /// RTSP 서버 실행 관리 서비스.
    /// 
    /// 현재 1단계에서는 MediaMTX를 RTSP 서버로 사용한다.
    /// 단, 향후 다른 RTSP 서버로 교체될 수 있으므로
    /// 서비스 이름은 MediaMtxService가 아니라 RtspServerService로 정의한다.
    /// 
    /// 주요 역할:
    /// 1. mediamtx.yml 생성
    /// 2. mediamtx_final_32bit.exe 실행
    /// 3. MediaMTX 로그 수신
    /// 4. MediaMTX 종료
    /// </summary>
    public class RtspServerService : IDisposable
    {
        private readonly PathProvider _pathProvider;
        private readonly ProcessRunner _processRunner;

        private bool _disposed;

        /// <summary>
        /// RTSP 서버 로그 수신 이벤트.
        /// View 또는 Presenter에서 이 이벤트를 구독하여 로그를 표시하거나 파일에 기록할 수 있다.
        /// </summary>
        public event Action<string> LogReceived;

        /// <summary>
        /// RTSP 서버 종료 이벤트.
        /// </summary>
        public event Action<int> Exited;

        public RtspServerService(PathProvider pathProvider)
        {
            if (pathProvider == null)
                throw new ArgumentNullException("pathProvider");

            _pathProvider = pathProvider;
            _processRunner = new ProcessRunner();

            _processRunner.OutputReceived += OnProcessOutputReceived;
            _processRunner.ErrorReceived += OnProcessErrorReceived;
            _processRunner.Exited += OnProcessExited;
        }

        /// <summary>
        /// 현재 RTSP 서버가 실행 중인지 여부.
        /// </summary>
        public bool IsRunning
        {
            get { return _processRunner.IsRunning; }
        }

        /// <summary>
        /// RTSP 서버를 시작한다.
        /// 
        /// 처리 순서:
        /// 1. External 폴더 확인
        /// 2. MediaMTX 실행 파일 확인
        /// 3. mediamtx.yml 생성
        /// 4. mediamtx_final_32bit.exe 실행
        /// </summary>
        public void Start(AppConfig config)
        {
            if (_disposed)
                throw new ObjectDisposedException("RtspServerService");

            if (config == null)
                throw new ArgumentNullException("config");

            if (config.RtspServer == null)
                throw new InvalidOperationException("RTSP 서버 설정이 없습니다.");

            if (IsRunning)
                return;

            _pathProvider.EnsureDirectories();

            string exePath = GetServerExePath(config.RtspServer);
            string configPath = GetServerConfigPath(config.RtspServer);

            if (!File.Exists(exePath))
            {
                throw new FileNotFoundException(
                    "MediaMTX 실행 파일을 찾을 수 없습니다. External 폴더에 파일을 배치하세요.",
                    exePath);
            }

            WriteMediaMtxConfig(config, configPath);


            string arguments = "\"" + configPath + "\"";

            RaiseLog("MediaMTX 실행 파일: " + exePath);
            RaiseLog("MediaMTX 설정 파일: " + configPath);
            RaiseLog("MediaMTX 실행 인자: " + arguments);

            _processRunner.Start(
                exePath,
                arguments,
                _pathProvider.ExternalDirectory);
        }

        /// <summary>
        /// RTSP 서버를 종료한다.
        /// </summary>
        public void Stop()
        {
            if (_disposed)
                return;

            if (!IsRunning)
                return;

            RaiseLog("MediaMTX 종료 요청");

            _processRunner.StopMediaMtx();
        }

        /// <summary>
        /// MediaMTX 실행 파일 전체 경로를 반환한다.
        /// </summary>
        private string GetServerExePath(RtspServerConfig config)
        {
            string exeName = config.ServerExeName;

            if (string.IsNullOrWhiteSpace(exeName))
                exeName = "mediamtx_final_32bit.exe";

            return Path.Combine(_pathProvider.ExternalDirectory, exeName);
        }

        /// <summary>
        /// MediaMTX 설정 파일 전체 경로를 반환한다.
        /// </summary>
        private string GetServerConfigPath(RtspServerConfig config)
        {
            string configFileName = config.ConfigFileName;

            if (string.IsNullOrWhiteSpace(configFileName))
                configFileName = "mediamtx.yml";

            return Path.Combine(_pathProvider.ExternalDirectory, configFileName);
        }

        /// <summary>
        /// MediaMTX 설정 파일을 생성한다.
        /// 
        /// 현재 1단계에서는 FFmpeg가 MediaMTX로 publish하고,
        /// VLC/NVR이 MediaMTX에서 read하는 구조이다.
        /// 
        /// 생성 예:
        /// 
        /// logLevel: info
        /// rtspAddress: ":8554"
        /// rtmpAddress: ":1935"
        /// 
        /// paths:
        ///   poscam:
        ///     source: publisher
        ///   poscam_1:
        ///     source: publisher
        /// </summary>
        private void WriteMediaMtxConfig(AppConfig appConfig, string configPath)
        {
            if (appConfig.RtspServer.RtspPort <= 0 || appConfig.RtspServer.RtspPort > 65535)
                throw new InvalidOperationException("RTSP 포트 값이 올바르지 않습니다.");

            if (appConfig.RtspServer.RtmpPort <= 0 || appConfig.RtspServer.RtmpPort > 65535)
                throw new InvalidOperationException("RTMP 포트 값이 올바르지 않습니다.");

            List<string> paths = BuildRtspPaths(appConfig);

            for (int i = 0; i < paths.Count; i++)
            {
                RaiseLog("MediaMTX path 생성 대상: " + paths[i]);
            }

            StringBuilder sb = new StringBuilder();

            sb.AppendLine("logLevel: info");
            sb.AppendLine();
            sb.AppendLine("rtspAddress: \":" + appConfig.RtspServer.RtspPort + "\"");
            sb.AppendLine("rtmpAddress: \":" + appConfig.RtspServer.RtmpPort + "\"");
            sb.AppendLine();
            sb.AppendLine("paths:");

            foreach (string path in paths)
            {
                sb.AppendLine("  " + path + ":");
                sb.AppendLine("    source: publisher");

                /*
                 * 1단계에서는 ONVIF 계정 정보를 RTSP Read 인증에도 사용한다.
                 * 
                 * FFmpeg publish는 로컬에서 수행하므로 별도 인증을 걸지 않는다.
                 * VLC/NVR read 접근에만 계정/비밀번호를 요구한다.
                 */
                if (appConfig.Onvif != null &&
                    !string.IsNullOrWhiteSpace(appConfig.Onvif.UserId) &&
                    !string.IsNullOrWhiteSpace(appConfig.Onvif.Password))
                {
                    sb.AppendLine("    readUser: \"" + EscapeYamlValue(appConfig.Onvif.UserId) + "\"");
                    sb.AppendLine("    readPass: \"" + EscapeYamlValue(appConfig.Onvif.Password) + "\"");
                }
            }

            string directory = Path.GetDirectoryName(configPath);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            /*
             * Windows 7 메모장에서 줄바꿈이 깨져 보이지 않도록
             * UTF-8 BOM 없이 저장하기보다 기본 UTF-8 또는 시스템 인코딩을 사용할 수 있다.
             * 여기서는 MediaMTX가 정상적으로 읽을 수 있도록 UTF-8로 저장한다.
             */
            File.WriteAllText(configPath, sb.ToString(), Encoding.UTF8);

            RaiseLog("MediaMTX 설정 파일 생성 완료");
        }

        /// <summary>
        /// AppConfig에 등록된 RTSP publish 경로 목록을 만든다.
        /// 
        /// 각 StreamConfig마다 Main/Sub 경로를 모두 생성한다.
        /// 
        /// 예:
        /// Stream0:
        /// - poscam
        /// - poscam_sub
        /// 
        /// Stream1:
        /// - poscam_1
        /// - poscam_1_sub
        /// 
        /// 주의:
        /// Main/Sub IsEnabled 여부와 무관하게 path는 만들어 둔다.
        /// MediaMTX는 path가 있어야 FFmpeg publish 요청을 받을 수 있다.
        /// </summary>
        private List<string> BuildRtspPaths(AppConfig appConfig)
        {
            List<string> result = new List<string>();

            if (appConfig != null && appConfig.Streams != null)
            {
                foreach (StreamConfig stream in appConfig.Streams)
                {
                    if (stream == null)
                        continue;

                    /*
                     * Stream 기본 RTSP 경로.
                     * 예:
                     * Stream0 → poscam
                     * Stream1 → poscam_1
                     */
                    string basePath = NormalizeRtspPath(stream.RtspPath);

                    if (string.IsNullOrWhiteSpace(basePath))
                    {
                        basePath = stream.StreamNo == 0
                            ? "poscam"
                            : "poscam_" + stream.StreamNo;
                    }

                    /*
                     * MainStream 경로.
                     * MainStream 설정이 있으면 해당 값을 우선 사용하고,
                     * 없으면 basePath를 사용한다.
                     */
                    string mainPath = "";

                    if (stream.MainStream != null &&
                        !string.IsNullOrWhiteSpace(stream.MainStream.RtspPath))
                    {
                        mainPath = stream.MainStream.RtspPath;
                    }
                    else
                    {
                        mainPath = basePath;
                    }

                    /*
                     * SubStream 경로.
                     * SubStream 설정이 있으면 해당 값을 우선 사용하고,
                     * 없으면 basePath + "_sub"를 사용한다.
                     */
                    string subPath = "";

                    if (stream.SubStream != null &&
                        !string.IsNullOrWhiteSpace(stream.SubStream.RtspPath))
                    {
                        subPath = stream.SubStream.RtspPath;
                    }
                    else
                    {
                        subPath = basePath + "_sub";
                    }

                    AddRtspPath(result, basePath);
                    AddRtspPath(result, mainPath);
                    AddRtspPath(result, subPath);
                }
            }

            if (result.Count == 0)
                result.Add("poscam");

            return result;
        }

        /// <summary>
        /// RTSP 경로를 정리한 뒤 목록에 추가한다.
        /// 
        /// 이미 같은 경로가 있으면 중복 추가하지 않는다.
        /// </summary>
        /// <param name="paths">
        /// RTSP 경로 목록.
        /// </param>
        /// <param name="path">
        /// 추가할 RTSP 경로.
        /// </param>
        private void AddRtspPath(List<string> paths, string path)
        {
            if (paths == null)
                return;

            string normalizedPath = NormalizeRtspPath(path);

            if (string.IsNullOrWhiteSpace(normalizedPath))
                return;

            if (!ContainsRtspPath(paths, normalizedPath))
                paths.Add(normalizedPath);
        }

        /// <summary>
        /// RTSP 경로 목록에 같은 경로가 이미 있는지 확인한다.
        /// 
        /// 대소문자는 구분하지 않는다.
        /// </summary>
        /// <param name="paths">
        /// RTSP 경로 목록.
        /// </param>
        /// <param name="path">
        /// 확인할 RTSP 경로.
        /// </param>
        /// <returns>
        /// true: 이미 존재함
        /// false: 존재하지 않음
        /// </returns>
        private bool ContainsRtspPath(List<string> paths, string path)
        {
            if (paths == null)
                return false;

            for (int i = 0; i < paths.Count; i++)
            {
                if (string.Equals(
                    paths[i],
                    path,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// RTSP 경로를 MediaMTX path 이름으로 사용할 수 있도록 정리한다.
        /// 
        /// 예:
        /// /poscam  -> poscam
        /// poscam/  -> poscam
        /// </summary>
        private string NormalizeRtspPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "";

            path = path.Trim();

            while (path.StartsWith("/"))
                path = path.Substring(1);

            while (path.EndsWith("/"))
                path = path.Substring(0, path.Length - 1);

            /*
             * MediaMTX path 이름은 단순하게 유지하는 것이 좋다.
             * 1단계에서는 영문, 숫자, _, - 정도만 허용한다.
             */
            foreach (char ch in path)
            {
                bool valid =
                    char.IsLetterOrDigit(ch) ||
                    ch == '_' ||
                    ch == '-';

                if (!valid)
                {
                    throw new InvalidOperationException(
                        "RTSP 경로에는 영문, 숫자, _, - 만 사용할 수 있습니다. 경로: " + path);
                }
            }

            return path;
        }

        private void OnProcessOutputReceived(string line)
        {
            RaiseLog("[OUT] " + line);
        }

        private void OnProcessErrorReceived(string line)
        {
            /*
             * MediaMTX는 대부분 stdout으로 로그를 출력하지만,
             * 빌드/환경에 따라 stderr로 출력될 수도 있으므로 같이 기록한다.
             */
            RaiseLog("[ERR] " + line);
        }

        private void OnProcessExited(int exitCode)
        {
            RaiseLog("MediaMTX 종료됨. ExitCode=" + exitCode);

            Action<int> handler = Exited;
            if (handler != null)
                handler(exitCode);
        }

        private void RaiseLog(string message)
        {
            Action<string> handler = LogReceived;

            if (handler != null)
                handler(message);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            try
            {
                Stop();
            }
            catch
            {
            }

            try
            {
                _processRunner.OutputReceived -= OnProcessOutputReceived;
                _processRunner.ErrorReceived -= OnProcessErrorReceived;
                _processRunner.Exited -= OnProcessExited;
                _processRunner.Dispose();
            }
            catch
            {
            }

            _disposed = true;
        }
        /// <summary>
        /// YAML 문자열 값에 들어갈 수 있는 따옴표를 안전하게 처리한다.
        /// </summary>
        private string EscapeYamlValue(string value)
        {
            if (value == null)
                return "";

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }


}