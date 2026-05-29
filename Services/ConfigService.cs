using System;
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

            LoadStream(ini, config, 0);
            LoadStream(ini, config, 1);
            LoadOnvif(ini, config);
            LoadAuth(ini, config);
            LoadOperation(ini, config);
            LoadRtspServer(ini, config);

            return config;
        }

        /// <summary>
        /// 설정 파일을 저장한다.
        /// </summary>
        public void Save(AppConfig config)
        {
            if (config == null)
                throw new ArgumentNullException("config");

            _pathProvider.EnsureDirectories();

            IniFileHelper ini = new IniFileHelper(_pathProvider.AppConfigFilePath);

            // 스트림은 0번, 1번을 기본으로 저장한다.
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
        }

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

            // 모니터는 실제 DISPLAY 이름이나 좌표를 저장하지 않고,
            // 주 모니터/보조 모니터 역할만 저장한다.
            stream.MonitorRole = ini.ReadString(section, "MonitorRole", stream.MonitorRole);

            // 화면명은 사용자가 직접 입력하는 업무 식별값이다.
            // 인증서버나 관리 화면에서 사용할 수 있으므로 반드시 저장한다.
            stream.ScreenName = ini.ReadString(section, "ScreenName", stream.ScreenName);

            stream.OnvifPort = ini.ReadInt(section, "OnvifPort", stream.OnvifPort);
            stream.Fps = ini.ReadInt(section, "Fps", stream.Fps);
            stream.Bitrate = ini.ReadString(section, "Bitrate", stream.Bitrate);
            stream.Codec = ini.ReadString(section, "Codec", stream.Codec);
            stream.RtspPath = ini.ReadString(section, "RtspPath", stream.RtspPath);
        }

        private void SaveStream(IniFileHelper ini, StreamConfig stream)
        {
            if (stream == null)
                return;

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
    }
}