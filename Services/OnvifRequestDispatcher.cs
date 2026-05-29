using System;
using pccam_32.Models;

namespace pccam_32.Services
{
    /// <summary>
    /// ONVIF SOAP 요청을 분석하여 적절한 응답 생성 메서드로 분기하는 서비스.
    /// 
    /// 실제 HTTP 서버는 요청 본문을 문자열로 읽은 뒤 이 클래스에 전달한다.
    /// 이 클래스는 요청 XML에 포함된 ONVIF 동작명을 기준으로 응답 XML을 반환한다.
    /// </summary>
    public class OnvifRequestDispatcher
    {
        private readonly OnvifSoapResponseBuilder _responseBuilder;

        /// <summary>
        /// ONVIF 요청 분기 처리기를 생성한다.
        /// </summary>
        /// <param name="responseBuilder">ONVIF SOAP 응답 생성기.</param>
        public OnvifRequestDispatcher(OnvifSoapResponseBuilder responseBuilder)
        {
            if (responseBuilder == null)
                throw new ArgumentNullException("responseBuilder");

            _responseBuilder = responseBuilder;
        }

        /// <summary>
        /// ONVIF SOAP 요청 문자열을 분석하고 응답 XML을 반환한다.
        /// 
        /// 현재는 XML 파서를 사용하지 않고 요청 문자열에 포함된 동작명을 기준으로 분기한다.
        /// 초기 호환성 테스트 단계에서는 이 방식으로 충분하며,
        /// 이후 WS-Security나 정확한 SOAP Header 처리가 필요해지면 XML 파서 기반으로 확장한다.
        /// </summary>
        /// <param name="requestBody">HTTP 요청 본문 SOAP XML.</param>
        /// <param name="config">현재 PC CAM 설정.</param>
        /// <param name="host">NVR이 접근 가능한 PC CAM 호스트.</param>
        /// <param name="onvifPort">ONVIF HTTP 포트.</param>
        /// <returns>SOAP 응답 XML.</returns>
        public string Dispatch(
            string requestBody,
            AppConfig config,
            string host,
            int onvifPort)
        {
            string actionName = GetActionName(requestBody);

            if (string.Equals(actionName, "Empty", StringComparison.OrdinalIgnoreCase))
                return _responseBuilder.BuildSoapFault("요청 본문이 비어 있습니다.");

            if (string.Equals(actionName, "GetDeviceInformation", StringComparison.OrdinalIgnoreCase))
                return _responseBuilder.BuildGetDeviceInformationResponse();

            if (string.Equals(actionName, "GetSystemDateAndTime", StringComparison.OrdinalIgnoreCase))
                return _responseBuilder.BuildGetSystemDateAndTimeResponse();

            if (string.Equals(actionName, "GetServices", StringComparison.OrdinalIgnoreCase))
                return _responseBuilder.BuildGetServicesResponse(host, onvifPort);

            if (string.Equals(actionName, "GetScopes", StringComparison.OrdinalIgnoreCase))
                return _responseBuilder.BuildGetScopesResponse();

            if (string.Equals(actionName, "GetCapabilities", StringComparison.OrdinalIgnoreCase))
                return _responseBuilder.BuildGetCapabilitiesResponse(host, onvifPort);

            if (string.Equals(actionName, "GetProfiles", StringComparison.OrdinalIgnoreCase))
                return _responseBuilder.BuildGetProfilesResponse(config);

            if (string.Equals(actionName, "GetStreamUri", StringComparison.OrdinalIgnoreCase))
                return _responseBuilder.BuildGetStreamUriResponse(config, host, 0);

            return _responseBuilder.BuildSoapFault("지원하지 않는 ONVIF 요청입니다.");
        }

        /// <summary>
        /// 요청 본문에 특정 ONVIF 동작명이 포함되어 있는지 확인한다.
        /// 
        /// 네임스페이스 prefix가 tds, trt, 또는 임의 값일 수 있으므로
        /// 단순히 동작명 문자열 포함 여부로 판단한다.
        /// </summary>
        /// <param name="requestBody">요청 SOAP XML 문자열.</param>
        /// <param name="actionName">확인할 ONVIF 동작명.</param>
        /// <returns>true이면 해당 동작명이 포함됨.</returns>
        private bool ContainsAction(string requestBody, string actionName)
        {
            return requestBody.IndexOf(actionName, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// ONVIF SOAP 요청 본문에서 요청 액션명을 추출한다.
        /// 
        /// 현재 구현은 XML 파서를 사용하지 않고,
        /// 지원 대상 ONVIF 액션명이 요청 본문에 포함되어 있는지 확인한다.
        /// 
        /// NVR 제조사마다 namespace prefix가 다를 수 있으므로
        /// tds:GetProfiles, trt:GetProfiles처럼 prefix까지 비교하지 않고
        /// 액션명 문자열 포함 여부로 판단한다.
        /// </summary>
        /// <param name="requestBody">
        /// HTTP 요청 본문 SOAP XML.
        /// </param>
        /// <returns>
        /// 확인된 ONVIF 액션명.
        /// 지원하지 않는 요청이면 Unknown을 반환한다.
        /// </returns>
        public string GetActionName(string requestBody)
        {
            if (string.IsNullOrWhiteSpace(requestBody))
                return "Empty";

            if (ContainsAction(requestBody, "GetDeviceInformation"))
                return "GetDeviceInformation";

            if (ContainsAction(requestBody, "GetSystemDateAndTime"))
                return "GetSystemDateAndTime";

            if (ContainsAction(requestBody, "GetServices"))
                return "GetServices";

            if (ContainsAction(requestBody, "GetScopes"))
                return "GetScopes";

            if (ContainsAction(requestBody, "GetCapabilities"))
                return "GetCapabilities";

            if (ContainsAction(requestBody, "GetProfiles"))
                return "GetProfiles";

            if (ContainsAction(requestBody, "GetStreamUri"))
                return "GetStreamUri";

            return "Unknown";
        }
    }
}