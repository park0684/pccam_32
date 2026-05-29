using System.Collections.Generic;

namespace pccam_32.Models
{
    /// <summary>
    /// PC CAM 전체 설정 모델.
    /// 
    /// INI 파일의 설정값을 프로그램 내부에서 다루기 위한 루트 설정 객체이다.
    /// </summary>
    public class AppConfig
    {
        /// <summary>
        /// 스트림 설정 목록.
        /// 기본적으로 0번 주 모니터, 1번 보조 모니터 구성을 가진다.
        /// </summary>
        public List<StreamConfig> Streams { get; set; } = new List<StreamConfig>();

        /// <summary>
        /// ONVIF 설정.
        /// </summary>
        public OnvifConfig Onvif { get; set; } = new OnvifConfig();

        /// <summary>
        /// 인증 설정.
        /// </summary>
        public AuthConfig Auth { get; set; } = new AuthConfig();

        /// <summary>
        /// 운영 설정.
        /// </summary>
        public OperationConfig Operation { get; set; } = new OperationConfig();

        /// <summary>
        /// RTSP 서버 설정.
        /// </summary>
        public RtspServerConfig RtspServer { get; set; } = new RtspServerConfig();

        /// <summary>
        /// 기본 설정 객체를 생성한다.
        /// 
        /// Stream0:
        /// - 주 모니터
        /// - 기본 사용
        /// - rtsp://IP:8554/poscam
        /// 
        /// Stream1:
        /// - 보조 모니터
        /// - 기본 미사용
        /// - rtsp://IP:8554/poscam_1
        /// </summary>
        public static AppConfig CreateDefault()
        {
            var config = new AppConfig();

            config.Streams.Add(new StreamConfig
            {
                IsEnabled = true,
                StreamNo = 0,
                MonitorRole = "Primary",
                ScreenName = "POS_MAIN",
                OnvifPort = 8080,
                Fps = 5,
                Bitrate = "1200k",
                Codec = "H264",
                RtspPath = "poscam"
            });

            config.Streams.Add(new StreamConfig
            {
                IsEnabled = false,
                StreamNo = 1,
                MonitorRole = "Secondary",
                ScreenName = "POS_SUB",
                OnvifPort = 8081,
                Fps = 5,
                Bitrate = "1200k",
                Codec = "H264",
                RtspPath = "poscam_1"
            });

            return config;
        }
    }
}