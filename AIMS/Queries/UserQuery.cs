using AIMS.Data;
using AIMS.Models;
using Microsoft.EntityFrameworkCore;

public class UserQuery
{
    private readonly AimsDbContext _db;
    public UserQuery(AimsDbContext db) => _db = db;

    public async Task<List<GetUserDto>> GetAllUsersAsync()
    {
        // Example query, adjust as needed
        return await _db.Users
            .Select(u => new GetUserDto
            {
                UserID = u.UserID,
                FullName = u.FullName,
                Email = u.Email,
                EmployeeNumber = u.EmployeeNumber,
                Role = u.Role.RoleName,
                SupervisorID = u.SupervisorID
            })
            .ToListAsync();
    }
    public async Task<List<GetUserDto>> GetFirstNUsers(int n)
    {
        return await _db.Users.Select(u => new GetUserDto
        {
            UserID = u.UserID,
            FullName = u.FullName,
            EmployeeNumber = u.EmployeeNumber,
        }).Take(n).ToListAsync();
    }
    public async Task<List<GetUserDto>> SearchUserByName(string searchString)
    {
        var users = await _db.Users
            .Where(u => u.FullName.Contains(searchString))
            .Select(u => new GetUserDto
            {
                UserID = u.UserID,
                FullName = u.FullName,
                EmployeeNumber = u.EmployeeNumber,
            })
            // limit to to the top 20 rseluts
            .Take(20)
            .ToListAsync();

        return users;
    }
}


public class GetUserDto
{
    // PK
    public int UserID { get; set; }

    // Columns
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; } = string.Empty;
    public string EmployeeNumber { get; set; } = string.Empty; // e.g. "28809"

    public int? SupervisorID { get; set; }

    public String? Role { get; set; } = string.Empty;

}
