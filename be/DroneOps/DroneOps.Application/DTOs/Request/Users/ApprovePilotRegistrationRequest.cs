using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DroneOps.Application.DTOs.Request.Users
{
    public class ApprovePilotRegistrationRequest
    {
        /// <summary>
        /// Ghi chú khi duyệt đơn (nếu có)
        /// </summary>
        public string? Note { get; set; }
    }
}