using System;
using System.Collections.Generic;
using System.IO;
using pccam_32.Infrastructure;
using pccam_32.Models;

namespace pccam_32.Services
{
    /// <summary>
    /// PC CAM 설정 저장/로드 서비스.
    /// 
    /// 설정 파일은 config\pccam.ini 파일을 사용한다.
    /// JSON이나 외부 DLL 없이 Windows 기본 INI 방식을 사용한다.
    /// </summary>
    public class ConfigService
    {
        private readonly PathProvider _pathProvider;

        public ConfigService(PathProvider pathProvider)
        {
            if (pathProvider == null)
                throw new ArgumentNullException("pathProvider");

            _pathProvider = pathProvider;
        }

        /// <summary>
        /// 설정 파일을 로드한다.
        /// 
        /// 파일이 없으면 기본 설정을 생성하고 저장한 뒤 반환한다.
        /// 파일이 있으면 Stream0, Stream1처럼 존재하는 Stream 섹션을 순서대로 로드한다.
        /// </summary>
        public AppConfig Load()
        {
            _pathProvider.EnsureDirectories();

            IniFileHelper ini = new IniFileHelper(_pathProvider.AppConfigFilePath);

            if (!ini.Exists())
            {
                AppConfig defaultConfig = AppConfig.CreateDefault();
                Save(defaultConfig);
                return defaultConfig;
            }

            AppConfig config = AppConfig.CreateDefault();

            /*
             * AppConfig.CreateDefault()에서 생성된 기본 Stream 목록을 비우고,
             * 실제 INI 파일에 존재하는 Stream 섹션을 다시 로드한다.
             * 
             * 기존 방식처럼 Stream0, Stream1만 고정 로드하지 않는다.
             */
            if (config.Streams == null)
                config.Streams = new List<StreamConfig>();
            else
                config.Streams.Clear();

            LoadStreams(ini, config);

            if (config.Streams == null || config.Streams.Count == 0)
                config.Streams = AppConfig.CreateDefault().Streams;

            LoadOnvif(ini, config);
            LoadAuth(ini, config);
            LoadOperation(ini, config);
            LoadRtspServer(ini, config);

            return config;
        }

        /// <summary>
        /// 설정 파일을 저장한다.
        /// 
        /// Stream 설정은 현재 config.Streams 개수만큼 저장한다.
        /// 저장 후 INI 파일의 섹션 사이에 빈 줄을 넣어 가독성을 높인다.
        /// </summary>
        public void Save(AppConfig config)
        {
            if (config == null)
                throw new ArgumentNullException("config");

            _pathProvider.EnsureDirectories();

            IniFileHelper ini = new IniFileHelper(_pathProvider.AppConfigFilePath);

            if (config.Streams == null || config.Streams.Count == 0)
                config.Streams = AppConfig.CreateDefault().Streams;

            for (int i = 0; i < config.Streams.Count; i++)
            {
                SaveStream(ini, config.Streams[i]);
            }

            SaveOnvif(ini, config.Onvif ?? new OnvifConfig());
            SaveAuth(ini, config.Auth ?? new AuthConfig());
            SaveOperation(ini, config.Operation ?? new OperationConfig());
            SaveRtspServer(ini, config.RtspServer ?? new RtspServerConfig());

            /*
             * IniFileHelper가 섹션 간 공백을 관리하지 않으므로,
             * 모든 설정 저장이 끝난 뒤 파일을 한 번 정리한다.
             */
            FormatIniSectionSpacing();
        }

        /// <summary>
        /// INI 파일에 존재하는 Stream 섹션을 순서대로 로드한다.
        /// 
        /// 예:
        /// [Stream0], [Stream1], [Stream2] ...
        /// 
        /// 중간 번호가 비어 있으면 그 이후는 로드하지 않는다.
        /// </summary>
        private void LoadStreams(IniFileHelper ini, AppConfig config)
        {
            const int maxStreamCount = 16;

            for (int streamNo = 0; streamNo < maxStreamCount; streamNo++)
            {
                if (!HasStreamSection(ini, streamNo))
                    break;

                LoadStream(ini, config, streamNo);
            }
        }

        /// <summary>
        /// 지정한 Stream 섹션이 INI 파일에 존재하는지 확인한다.
        /// 
        /// IniFileHelper에 섹션 존재 확인 기능이 없으므로,
        /// 주요 키를 sentinel 값으로 읽어 존재 여부를 판단한다.
        /// </summary>
        private bool HasStreamSection(IniFileHelper ini, int streamNo)
        {
            string section = "Stream" + streamNo;
            const string missing = "__PCCAM_MISSING__";

            string streamNoValue = ini.ReadString(section, "StreamNo", missing);
            if (streamNoValue != missing)
                return true;

            string enabledValue = ini.ReadString(section, "IsEnabled", missing);
            if (enabledValue != missing)
                return true;

            string rtspPathValue = ini.ReadString(section, "RtspPath", missing);
            if (rtspPathValue != missing)
                return true;

            string screenNameValue = ini.ReadString(section, "ScreenName", missing);
            if (screenNameValue != missing)
                return true;

            return false;
        }

        /// <summary>
        /// 지정한 Stream 섹션을 로드한다.
        /// </summary>
        private void LoadStream(IniFileHelper ini, AppConfig config, int streamNo)
        {
            string section = "Stream" + streamNo;

            while (config.Streams.Count <= streamNo)
            {
                config.Streams.Add(new StreamConfig
                {
                    StreamNo = config.Streams.Count
                });
            }

            StreamConfig stream = config.Streams[streamNo];

            stream.IsEnabled = ini.ReadBool(section, "IsEnabled", stream.IsEnabled);
            stream.StreamNo = ini.ReadInt(section, "StreamNo", stream.StreamNo);

            /*
             * MonitorRole은 Primary, Secondary, Monitor2 같은 역할명이다.
             */
            stream.MonitorRole = ini.ReadString(section, "MonitorRole", stream.MonitorRole);

            /*
             * ScreenName은 현재 단계에서 화면 식별명으로 사용한다.
             */
            stream.ScreenName = ini.ReadString(section, "ScreenName", stream.ScreenName);

            stream.OnvifPort = ini.ReadInt(section, "OnvifPort", stream.OnvifPort);
            stream.Fps = ini.ReadInt(section, "Fps", stream.Fps);
            stream.Bitrate = ini.ReadString(section, "Bitrate", stream.Bitrate);
            stream.Codec = ini.ReadString(section, "Codec", stream.Codec);
            stream.RtspPath = ini.ReadString(section, "RtspPath", stream.RtspPath);

            /*
            * Main/Sub Stream 품질 설정 로드.
            */
            stream.MainStream = LoadStreamQuality(
                ini,
                "Stream" + streamNo + ".Main",
                StreamQualityConfig.CreateMain(stream.RtspPath));

            stream.SubStream = LoadStreamQuality(
                ini,
                "Stream" + streamNo + ".Sub",
                StreamQualityConfig.CreateSub(stream.RtspPath + "_sub"));

            /*
             * Main/Sub RTSP 경로 보정.
             * SubStream 경로가 비어 있거나 MainStream과 같으면
             * 기본적으로 Main 경로 + "_sub" 형태로 보정한다.
             */
            if (stream.MainStream != null && stream.SubStream != null)
            {
                string mainPath = stream.MainStream.RtspPath;
                string subPath = stream.SubStream.RtspPath;

                if (string.IsNullOrWhiteSpace(subPath) ||
                    string.Equals(mainPath, subPath, StringComparison.OrdinalIgnoreCase))
                {
                    stream.SubStream.RtspPath =
                        string.IsNullOrWhiteSpace(stream.RtspPath)
                            ? "poscam_sub"
                            : stream.RtspPath + "_sub";
                }
            }
        }

        /// <summary>
        /// 지정한 Stream 설정을 INI 파일에 저장한다.
        /// </summary>
        private void SaveStream(IniFileHelper ini, StreamConfig stream)
        {
            if (stream == null)
                return;

            //저장 전 Main/Sub RTSP 경로 보정. Stream.RtspPath를 기준으로 Main/Sub 경로가 올바르게 설정되도록 보정한다.
            NormalizeStreamQualityPaths(stream);

            string section = "Stream" + stream.StreamNo;

            ini.WriteBool(section, "IsEnabled", stream.IsEnabled);
            ini.WriteInt(section, "StreamNo", stream.StreamNo);
            ini.WriteString(section, "MonitorRole", stream.MonitorRole);
            ini.WriteString(section, "ScreenName", stream.ScreenName);
            ini.WriteInt(section, "OnvifPort", stream.OnvifPort);
            ini.WriteInt(section, "Fps", stream.Fps);
            ini.WriteString(section, "Bitrate", stream.Bitrate);
            ini.WriteString(section, "Codec", stream.Codec);
            ini.WriteString(section, "RtspPath", stream.RtspPath);

            /*
            * Main/Sub Stream 품질 설정 저장.
            * 
            * 부모 Stream이 비활성화되어 있으면
            * Main/Sub도 설정 파일상 비활성화 상태로 저장한다.
            */
            StreamQualityConfig mainStream =
                stream.MainStream ?? StreamQualityConfig.CreateMain(stream.RtspPath);

            StreamQualityConfig subStream =
                stream.SubStream ?? StreamQualityConfig.CreateSub(stream.RtspPath + "_sub");

            SaveStreamQuality(
                ini,
                "Stream" + stream.StreamNo + ".Main",
                mainStream,
                stream.IsEnabled);

            SaveStreamQuality(
                ini,
                "Stream" + stream.StreamNo + ".Sub",
                subStream,
                stream.IsEnabled);
        }

        /// <summary>
        /// 품질별 스트림 설정을 INI에 저장한다.
        /// 
        /// 부모 Stream이 비활성화되어 있으면
        /// 품질 스트림도 실제 사용 불가 상태로 저장한다.
        /// </summary>
        private void SaveStreamQuality(
            IniFileHelper ini,
            string section,
            StreamQualityConfig quality,
            bool parentStreamEnabled)
        {
            if (quality == null)
                return;

            bool effectiveEnabled =
                parentStreamEnabled &&
                quality.IsEnabled;

            ini.WriteBool(section, "IsEnabled", effectiveEnabled);
            ini.WriteString(section, "RtspPath", quality.RtspPath);
            ini.WriteInt(section, "Fps", quality.Fps);
            ini.WriteString(section, "Bitrate", quality.Bitrate);
            ini.WriteInt(section, "Width", quality.Width);
            ini.WriteInt(section, "Height", quality.Height);
        }

        /// <summary>
        /// 품질별 스트림 설정을 INI에서 로드한다.
        /// 
        /// 해당 섹션이 없으면 기본값을 유지한다.
        /// </summary>
        private StreamQualityConfig LoadStreamQuality(
            IniFileHelper ini,
            string section,
            StreamQualityConfig defaultValue)
        {
            StreamQualityConfig quality = defaultValue ?? new StreamQualityConfig();

            quality.IsEnabled = ini.ReadBool(section, "IsEnabled", quality.IsEnabled);
            quality.RtspPath = ini.ReadString(section, "RtspPath", quality.RtspPath);
            quality.Fps = ini.ReadInt(section, "Fps", quality.Fps);
            quality.Bitrate = ini.ReadString(section, "Bitrate", quality.Bitrate);
            quality.Width = ini.ReadInt(section, "Width", quality.Width);
            quality.Height = ini.ReadInt(section, "Height", quality.Height);

            return quality;
        }

        private void LoadOnvif(IniFileHelper ini, AppConfig config)
        {
            if (config.Onvif == null)
                config.Onvif = new OnvifConfig();

            config.Onvif.IsEnabled = ini.ReadBool("Onvif", "IsEnabled", config.Onvif.IsEnabled);
            config.Onvif.UserId = ini.ReadString("Onvif", "UserId", config.Onvif.UserId);
            config.Onvif.Password = ini.ReadString("Onvif", "Password", config.Onvif.Password);
        }

        private void SaveOnvif(IniFileHelper ini, OnvifConfig onvif)
        {
            ini.WriteBool("Onvif", "IsEnabled", onvif.IsEnabled);
            ini.WriteString("Onvif", "UserId", onvif.UserId);
            ini.WriteString("Onvif", "Password", onvif.Password);
        }

        private void LoadAuth(IniFileHelper ini, AppConfig config)
        {
            if (config.Auth == null)
                config.Auth = new AuthConfig();

            config.Auth.LicenseKey = ini.ReadString("Auth", "LicenseKey", config.Auth.LicenseKey);
            config.Auth.DeviceName = ini.ReadString("Auth", "DeviceName", config.Auth.DeviceName);
            config.Auth.LastAuthResult = ini.ReadString("Auth", "LastAuthResult", config.Auth.LastAuthResult);

            string lastAuthAt = ini.ReadString("Auth", "LastAuthAt", "");

            DateTime parsed;
            if (DateTime.TryParse(lastAuthAt, out parsed))
                config.Auth.LastAuthAt = parsed;
            else
                config.Auth.LastAuthAt = null;
        }

        private void SaveAuth(IniFileHelper ini, AuthConfig auth)
        {
            ini.WriteString("Auth", "LicenseKey", auth.LicenseKey);
            ini.WriteString("Auth", "DeviceName", auth.DeviceName);
            ini.WriteString("Auth", "LastAuthResult", auth.LastAuthResult);

            if (auth.LastAuthAt.HasValue)
                ini.WriteString("Auth", "LastAuthAt", auth.LastAuthAt.Value.ToString("yyyy-MM-dd HH:mm:ss"));
            else
                ini.WriteString("Auth", "LastAuthAt", "");
        }

        private void LoadOperation(IniFileHelper ini, AppConfig config)
        {
            if (config.Operation == null)
                config.Operation = new OperationConfig();

            config.Operation.EnableDetailLog = ini.ReadBool("Operation", "EnableDetailLog", config.Operation.EnableDetailLog);
            config.Operation.AutoStart = ini.ReadBool("Operation", "AutoStart", config.Operation.AutoStart);
            config.Operation.PreventSleep = ini.ReadBool("Operation", "PreventSleep", config.Operation.PreventSleep);
            config.Operation.AutoStartStreaming = ini.ReadBool("Operation", "AutoStartStreaming", config.Operation.AutoStartStreaming);
        }

        private void SaveOperation(IniFileHelper ini, OperationConfig operation)
        {
            ini.WriteBool("Operation", "EnableDetailLog", operation.EnableDetailLog);
            ini.WriteBool("Operation", "AutoStart", operation.AutoStart);
            ini.WriteBool("Operation", "PreventSleep", operation.PreventSleep);
            ini.WriteBool("Operation", "AutoStartStreaming", operation.AutoStartStreaming);
        }

        private void LoadRtspServer(IniFileHelper ini, AppConfig config)
        {
            if (config.RtspServer == null)
                config.RtspServer = new RtspServerConfig();

            config.RtspServer.RtspPort = ini.ReadInt("RtspServer", "RtspPort", config.RtspServer.RtspPort);
            config.RtspServer.RtmpPort = ini.ReadInt("RtspServer", "RtmpPort", config.RtspServer.RtmpPort);
            config.RtspServer.ServerExeName = ini.ReadString("RtspServer", "ServerExeName", config.RtspServer.ServerExeName);
            config.RtspServer.ConfigFileName = ini.ReadString("RtspServer", "ConfigFileName", config.RtspServer.ConfigFileName);
        }

        private void SaveRtspServer(IniFileHelper ini, RtspServerConfig rtspServer)
        {
            ini.WriteInt("RtspServer", "RtspPort", rtspServer.RtspPort);
            ini.WriteInt("RtspServer", "RtmpPort", rtspServer.RtmpPort);
            ini.WriteString("RtspServer", "ServerExeName", rtspServer.ServerExeName);
            ini.WriteString("RtspServer", "ConfigFileName", rtspServer.ConfigFileName);
        }


        /// <summary>
        /// INI 파일의 섹션 사이에 빈 줄을 넣어 가독성을 높인다.
        /// 
        /// 예:
        /// [Stream0]
        /// ...
        /// 
        /// [Stream1]
        /// ...
        /// 
        /// [Onvif]
        /// ...
        /// </summary>
        private void FormatIniSectionSpacing()
        {
            string filePath = _pathProvider.AppConfigFilePath;

            if (string.IsNullOrWhiteSpace(filePath))
                return;

            if (!File.Exists(filePath))
                return;

            string[] lines = File.ReadAllLines(filePath);

            List<string> formatted = new List<string>();
            bool hasWrittenLine = false;
            bool previousLineWasBlank = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i] ?? "";
                string trimmed = line.Trim();

                if (trimmed.Length == 0)
                    continue;

                bool isSectionHeader =
                    trimmed.StartsWith("[") &&
                    trimmed.EndsWith("]");

                if (isSectionHeader && hasWrittenLine && !previousLineWasBlank)
                {
                    formatted.Add("");
                    previousLineWasBlank = true;
                }

                formatted.Add(line);
                hasWrittenLine = true;
                previousLineWasBlank = false;
            }

            File.WriteAllLines(filePath, formatted.ToArray());
        }

        /// <summary>
        /// StreamConfig의 Main/Sub RTSP 경로를 부모 Stream의 RtspPath 기준으로 보정한다.
        /// 
        /// 현재 정책:
        /// - Stream.RtspPath가 기준 경로이다.
        /// - MainStream.RtspPath는 Stream.RtspPath와 동일해야 한다.
        /// - SubStream.RtspPath는 Stream.RtspPath + "_sub" 이어야 한다.
        /// 
        /// 예:
        /// Stream1.RtspPath = poscam_1
        /// → Main = poscam_1
        /// → Sub  = poscam_1_sub
        /// </summary>
        private void NormalizeStreamQualityPaths(StreamConfig stream)
        {
            if (stream == null)
                return;

            string basePath = stream.RtspPath;

            if (string.IsNullOrWhiteSpace(basePath))
            {
                basePath = stream.StreamNo == 0
                    ? "poscam"
                    : "poscam_" + stream.StreamNo;

                stream.RtspPath = basePath;
            }

            if (stream.MainStream == null)
                stream.MainStream = StreamQualityConfig.CreateMain(basePath);

            if (stream.SubStream == null)
                stream.SubStream = StreamQualityConfig.CreateSub(basePath + "_sub");

            /*
             * 기존에 Stream1.Main이 poscam으로 잘못 저장된 경우를 보정한다.
             */
            stream.MainStream.RtspPath = basePath;

            /*
             * 기존에 Stream1.Sub가 poscam_sub로 잘못 저장된 경우를 보정한다.
             */
            stream.SubStream.RtspPath = basePath + "_sub";
        }
    }
}