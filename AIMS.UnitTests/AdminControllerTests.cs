using Microsoft.EntityFrameworkCore;
using AIMS.Controllers.Mvc;
using AIMS.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Graph.Models;
using Moq;

public class AdminControllerTests // Unit tests for AdminController
{
    [Fact]
    public async Task GetAzureAdUsers_ReturnsOk_WithUsers() // Test for GetAzureAdUsers method
    {
        var mockService = new Mock<IGraphUserService>(); // Mock the IGraphUserService
        mockService.Setup(s => s.GetUsersAsync(It.IsAny<string>())) // Mock the GetUsersAsync method
                   .ReturnsAsync(new List<User> { new User { DisplayName = "Jane Doe" } }); // Return a sample user

        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<AIMS.Data.AimsDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDb1")
            .Options;
        var dbContext = new AIMS.Data.AimsDbContext(options);
        var controller = new AdminController(mockService.Object, dbContext); // Create controller with mocked service

        var result = await controller.GetAzureAdUsers(null) as OkObjectResult; // Call the method

        Assert.NotNull(result); // Assert result is not null
        var users = Assert.IsType<List<User>>(result.Value); // Assert the value is a list of users
        Assert.Equal("Jane Doe", users[0].DisplayName); // Assert the user data
    }

    [Fact]
    public async Task GetAzureAdUsers_ForwardsSearchParameter_ToService() // Test that search parameter is forwarded
    {
        var mockService = new Mock<IGraphUserService>(); // Mock the IGraphUserService
        mockService.Setup(s => s.GetUsersAsync("query")) // Expect the search parameter "query"
                   .ReturnsAsync(new List<User> { new User { DisplayName = "Jane Doe" } }); // Return a sample user

        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<AIMS.Data.AimsDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDb2")
            .Options;
        var dbContext = new AIMS.Data.AimsDbContext(options);
        var controller = new AdminController(mockService.Object, dbContext);

        var result = await controller.GetAzureAdUsers("query") as OkObjectResult;

        Assert.NotNull(result);
        var users = Assert.IsType<List<User>>(result.Value);
        Assert.Single(users);
        mockService.Verify(s => s.GetUsersAsync("query"), Times.Once); // Verify the method was called with "query"
    }

    [Fact]
    public async Task GetAzureAdUsers_ReturnsOk_EmptyListWhenNoUsers() // Test for empty user list
    {
        var mockService = new Mock<IGraphUserService>(); // Mock the IGraphUserService
        mockService.Setup(s => s.GetUsersAsync(It.IsAny<string>())) // Mock the GetUsersAsync method
                   .ReturnsAsync(new List<User>()); // Return an empty list

        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<AIMS.Data.AimsDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDb3")
            .Options;
        var dbContext = new AIMS.Data.AimsDbContext(options);
        var controller = new AdminController(mockService.Object, dbContext); // Create controller with mocked service

        var result = await controller.GetAzureAdUsers(null) as OkObjectResult; // Call the method

        Assert.NotNull(result); // Assert result is not null
        var users = Assert.IsType<List<User>>(result.Value); // Assert the value is a list of users
        Assert.Empty(users); // Assert the list is empty
    }

    [Fact]
    public async Task GetAzureAdUsers_PropagatesServiceExceptions() // Test exception propagation
    {
        var mockService = new Mock<IGraphUserService>(); // Mock the IGraphUserService
        mockService.Setup(s => s.GetUsersAsync(It.IsAny<string>())) //  Mock the GetUsersAsync method
                   .ThrowsAsync(new InvalidOperationException("Graph failure")); // Simulate an exception

        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<AIMS.Data.AimsDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDb4")
            .Options;
        var dbContext = new AIMS.Data.AimsDbContext(options);
        var controller = new AdminController(mockService.Object, dbContext); // Create controller with mocked service

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await controller.GetAzureAdUsers(null)); // Assert the exception is propagated
    }

    [Fact]
    public async Task GetUserRoles_ReturnsOk_WithRoles() // Test for GetUserRoles method
    {
        var mockService = new Mock<IGraphUserService>(); // Mock the IGraphUserService
        mockService.Setup(s => s.GetUserRolesAsync("uid")) // Mock the GetUserRolesAsync method
                   .ReturnsAsync(new List<DirectoryObject> { new DirectoryRole { DisplayName = "Admin" } }); // Return a sample role

        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<AIMS.Data.AimsDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDb5")
            .Options;
        var dbContext = new AIMS.Data.AimsDbContext(options);
        var controller = new AdminController(mockService.Object, dbContext); // Create controller with mocked service

        var result = await controller.GetUserRoles("uid") as OkObjectResult; // Call the method

        Assert.NotNull(result);
        var roles = Assert.IsType<List<DirectoryObject>>(result.Value); // Assert the value is a list of roles
        Assert.Single(roles);
        Assert.Equal("Admin", ((DirectoryRole)roles[0]).DisplayName); // Assert the role data
    }

    [Fact]
    public async Task Index_ReturnsView() // Test for Index method
    {
        var options = new DbContextOptionsBuilder<AIMS.Data.AimsDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDb6")
            .Options;
        var dbContext = new AIMS.Data.AimsDbContext(options);
        var controller = new AdminController(Mock.Of<IGraphUserService>(), dbContext); // Use a mock service
        var result = await controller.Index(); // Call the method
        Assert.IsType<ViewResult>(result); // Assert the result is a ViewResult
    }
}
