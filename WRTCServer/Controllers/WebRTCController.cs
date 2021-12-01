using Microsoft.AspNetCore.Mvc;
using SIPSorcery.Net;
using System.Threading.Tasks;

namespace WRTCServer.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WebRTCController : ControllerBase
    {
        private readonly IPeerConnectionManager _peerConnectionManager;

        public WebRTCController(IPeerConnectionManager peerConnectionManager)
        {
            _peerConnectionManager = peerConnectionManager;
        }

        [HttpGet, Route("get_offer")]
        public async Task<IActionResult> GetOffer()
        {
            var (offer, id) = await _peerConnectionManager.CreateServerOffer();
            return Ok(new { id, offer = offer.sdp.ToString() });
        }


        [HttpPost, Route("set_remote/{id}")]
        public IActionResult SetRemoteDescription([FromRoute] string id, [FromBody] RTCSessionDescriptionInit rtcSessionDescriptionInit)
        {
            _peerConnectionManager.SetRemoteDescription(id, rtcSessionDescriptionInit);
            return Ok();
        }

        [HttpPost, Route("add_candidate/{id}")]
        public IActionResult AddIceCandidate([FromRoute] string id, [FromBody] RTCIceCandidateInit iceCandidate)
        {
            _peerConnectionManager.AddIceCandidate(id, iceCandidate);
            return Ok();
        }

        [HttpGet, Route("get_candidates/{id}")]
        public IActionResult GetIceResults([FromRoute] string id)
        {
            _peerConnectionManager.GetIceResults(id);
            return Ok();
        }
    }
}
