namespace Flowboard_Project_Management_System_Backend.Models
{
    public class LoginRequest
    {
        public string? UserNameOrEmail { get; set; }  // Allows login using username OR email
        public string? Password { get; set; }
    }
}
