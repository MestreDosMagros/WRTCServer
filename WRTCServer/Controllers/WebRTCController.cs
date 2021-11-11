using Microsoft.AspNetCore.Mvc;
using SIPSorcery.Net;
using System;

namespace WRTCServer.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WebRTCController : Controller
    {
        private readonly IPeerConnectionManager _peerConnectionManager;

        public WebRTCController(IPeerConnectionManager peerConnectionManager)
        {
            _peerConnectionManager = peerConnectionManager;
        }

        [HttpGet, Route("get_offer")]
        public IActionResult GetOffer()
        {
            var id = Guid.NewGuid().ToString();
            return Ok(new { id, offer = _peerConnectionManager.CreateServerOffer(id) });
        }


        [HttpPost, Route("set_remote/{id}")]
        public IActionResult SetRemoteDescription([FromRoute] string id, [FromBody] RTCSessionDescriptionInit rtcSessionDescriptionInit)
        {
            _peerConnectionManager.SetRemoteDescription(id, rtcSessionDescriptionInit);
            //return Ok(_peerConnectionManager.Get(id));
            return Ok();
        }

        [HttpPost, Route("add_candidate/{id}")]
        public IActionResult AddIceCandidate([FromRoute] string id, [FromBody] RTCIceCandidateInit iceCandidate)
        {
            _peerConnectionManager.AddIceCandidate(id, iceCandidate);
            //return Ok(_peerConnectionManager.Get(id));
            return Ok();
        }

        [HttpGet, Route("get_candidates/{id}")]
        public IActionResult GetIceResults([FromRoute] string id)
        {
            _peerConnectionManager.GetIceResults(id);
            //return Ok(_peerConnectionManager.Get(id));
            return Ok();
        }
    }
}
