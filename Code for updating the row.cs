using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;

public class CPHInline
{
    public bool Execute()
    {
        try
        {
            // Retrieve the Notion database ID
            string notionDatabaseId = CPH.GetGlobalVar<string>("notionDatabaseId");
            if (string.IsNullOrEmpty(notionDatabaseId))
            {
                CPH.LogInfo("Error: Notion database ID is not set.");
                return false;
            }

            string accessToken = CPH.GetGlobalVar<string>("notionAccessToken");

            // Query Notion to find all existing entries
            JObject allEntriesData = new JObject(); // Empty filter to get all entries
            string allEntriesResponse = NotionQueryAsync(notionDatabaseId, accessToken, allEntriesData).GetAwaiter().GetResult();
            JObject allEntriesResult = JObject.Parse(allEntriesResponse);

            // Create a list of users and their current Kills values
            List<UserEntry> users = ((JArray)allEntriesResult["results"]).Select(result => new UserEntry
            {
                UserId = result["properties"]["User ID"]["rich_text"][0]["text"]["content"].ToString(),
                Kills = (int)(result["properties"]["Kills"]["number"] ?? 0),
                PageId = result["id"].ToString()
            }).ToList();

            // Sort users by Kills in descending order and assign placements
            var sortedUsers = users.OrderByDescending(u => u.Kills).ToList();

            // Update placements for all users
            for (int i = 0; i < sortedUsers.Count; i++)
            {
                int placement = i + 1;
                var userToUpdate = sortedUsers[i];

                // Log the placement value before the update
                CPH.LogInfo($"Setting placement for user {userToUpdate.UserId} to {placement}");

                // Update the placement for each user in the database
                JObject placementData = new JObject
                {
                    ["properties"] = new JObject
                    {
                        ["Placement"] = new JObject
                        {
                            ["number"] = placement
                        }
                    }
                };

                // Send the update request for placement
                if (!string.IsNullOrEmpty(userToUpdate.PageId))
                {
                    string placementResponse = NotionUpdatePageAsync(userToUpdate.PageId, accessToken, placementData).GetAwaiter().GetResult();
                    CPH.LogInfo($"Updated placement to {placement} for user with User ID {userToUpdate.UserId}. Response: {placementResponse}");
                }
                else
                {
                    CPH.LogInfo($"Error: PageId not set for user {userToUpdate.UserId}.");
                }
            }
        }
        catch (Exception ex)
        {
            CPH.LogInfo($"Error: {ex.Message}");
            return false;
        }

        return true;
    }

    // Simple class to hold user data
    public class UserEntry
    {
        public string UserId { get; set; }
        public int Kills { get; set; }
        public string PageId { get; set; }
    }

    // Asynchronous method to query the Notion API
    public static async Task<string> NotionQueryAsync(string databaseId, string apiKey, JObject data)
    {
        using (var client = new HttpClient())
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            client.DefaultRequestHeaders.Add("Notion-Version", "2022-06-28");

            var url = $"https://api.notion.com/v1/databases/{databaseId}/query";
            var jsonContent = data.ToString();

            var request = new HttpRequestMessage
            {
                RequestUri = new Uri(url),
                Method = HttpMethod.Post,
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };

            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var responseContent = await response.Content.ReadAsStringAsync();
            return responseContent;
        }
    }

    // Asynchronous method to update a page in the Notion API
    public static async Task<string> NotionUpdatePageAsync(string pageId, string apiKey, JObject data)
    {
        using (var client = new HttpClient())
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            client.DefaultRequestHeaders.Add("Notion-Version", "2022-06-28");

            var url = $"https://api.notion.com/v1/pages/{pageId}";
            var jsonContent = data.ToString();

            var method = new HttpMethod("PATCH");
            var request = new HttpRequestMessage
            {
                RequestUri = new Uri(url),
                Method = method,
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };

            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var responseContent = await response.Content.ReadAsStringAsync();
            return responseContent;
        }
    }
}
