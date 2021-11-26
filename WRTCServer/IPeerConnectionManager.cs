using SIPSorcery.Net;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WRTCServer
{
    public interface IPeerConnectionManager
    {
        RTCPeerConnection Get(string id);
        List<RTCIceCandidate> GetIceResults(string id);
        Task<(RTCSessionDescriptionInit, string)> CreateServerOffer();
        void AddIceCandidate(string id, RTCIceCandidateInit iceCandidate);
        void SetRemoteDescription(string id, RTCSessionDescriptionInit rtcSessionDescriptionInit);
    }
}
