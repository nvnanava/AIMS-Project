using System;
using System.Data;
using System.Linq;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AIMS.Tests.Integration;

[Collection("API Test Collection")]
public class AssetsAPITests
{
    private readonly HttpClient _client;
    public AssetsAPITests(APiTestFixture fixture)
    {
        _client = fixture._webFactory.CreateClient();
        _client.BaseAddress = new Uri("https://localhost:5119");
    }

    [Fact]
    public async Task Unique_NoParams_ReturnSuccessAndList()
    {
        // Arrange
        // no parameters to pass

        List<string> expectedList = new List<string>
        {
            "Charging Cable",
            "Desktop",
            "Headset",
            "Laptop",
            "Monitor",
            "Software"
        };

        // Act
        var response = await _client.GetAsync("/api/assets/types/unique");

        response.EnsureSuccessStatusCode();

        // Assert
        var res_list = await JsonSerializer.DeserializeAsync<List<string>>(await response.Content.ReadAsStreamAsync());

        // make sure that data is returned
        Assert.NotNull(res_list);

        // make sure the counts match
        Assert.Equal(expectedList.Count, res_list.Count);


        // make sure data and order match
        Assert.True(expectedList.SequenceEqual(res_list));

    }

}



