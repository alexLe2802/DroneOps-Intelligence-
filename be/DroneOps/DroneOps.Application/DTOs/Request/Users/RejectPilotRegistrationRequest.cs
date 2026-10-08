using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DroneOps.Application.DTOs.Request.Users
{
    public class RejectPilotRegistrationRequest
    {
        /// <summary>
        /// Lý do từ chối đơn đăng ký của Pilot (bắt buộc nhập)
        /// </summary>
        public string Reason { get; set; } = string.Empty;
    }
}