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
            // Retrieve the Notion database ID from global variables in Streamer.bot
            string notionDatabaseId = CPH.GetGlobalVar<string>("notionDatabaseId");

            // Ensure the notionDatabaseId is not null or empty
            if (string.IsNullOrEmpty(notionDatabaseId))
            {
                CPH.LogInfo("Error: Notion database ID is not set.");
                return false;
            }

            // Retrieve the correct targetUserId
            string targetUserId = CPH.GetGlobalVar<string>("targetUserId");
            if (string.IsNullOrEmpty(targetUserId))
            {
                // Try to get the targetUserId from arguments if not found in global variables
                CPH.TryGetArg("targetUserId", out targetUserId);
            }
            if (string.IsNullOrEmpty(targetUserId))
            {
                targetUserId = "UnknownUserId"; // Fallback value if targetUserId is not provided
            }
            CPH.LogInfo($"Retrieved targetUserId: {targetUserId}"); // Log the userId for debugging

            // Retrieve the correct targetUserName
            string targetUserName = CPH.GetGlobalVar<string>("targetUserName");
            if (string.IsNullOrEmpty(targetUserName))
            {
                // Try to get the targetUserName from arguments if not found in global variables
                CPH.TryGetArg("targetUserName", out targetUserName);
            }
            if (string.IsNullOrEmpty(targetUserName))
            {
                targetUserName = "UnknownUserName"; // Fallback value if targetUserName is not provided
            }
            CPH.LogInfo($"Retrieved targetUserName: {targetUserName}"); // Log the userName for debugging

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

            // Find the user if they already exist
            UserEntry user = users.FirstOrDefault(u => u.UserId == targetUserId);
            if (user != null)
            {
                // If user has only 1 kill, archive their row from the Notion database
                if (user.Kills == 1)
                {
                    // Log the archiving of the user
                    CPH.LogInfo($"User {targetUserName} has only 1 kill. Archiving their row.");

                    // Send a request to archive the row
                    string archiveResponse = NotionArchivePageAsync(user.PageId, accessToken).GetAwaiter().GetResult();
                    CPH.LogInfo($"Archived {targetUserName}'s row from Notion. Response: {archiveResponse}");

                    // Send a message to confirm the archiving
                    CPH.SendMessage($"{targetUserName} has been removed from the leaderboard.");
                }
                else
                {
                    // Decrement the Kills count for the user
                    user.Kills -= 1;

                    JObject updateData = new JObject
                    {
                        ["properties"] = new JObject
                        {
                            ["Kills"] = new JObject
                            {
                                ["number"] = user.Kills
                            }
                        }
                    };

                    // Log the updated data
                    CPH.LogInfo($"Updating Kills to {user.Kills} for user {targetUserName}.");

                    // Send the update request
                    string updateResponse = NotionUpdatePageAsync(user.PageId, accessToken, updateData).GetAwaiter().GetResult();
                    CPH.LogInfo($"Notion update response: {updateResponse}");
                    CPH.SendMessage($"Updated user {targetUserName}'s Kills count to {user.Kills} in Notion.");
                }
            }
            else
            {
                // If the user doesn't exist, log and notify
                CPH.LogInfo($"User {targetUserName} not found in the database.");
                CPH.SendMessage($"{targetUserName} is not on the leaderboard.");
            }

            // Sort users by Kills in descending order and assign placements
            var sortedUsers = users.OrderByDescending(u => u.Kills).ToList();
            for (int i = 0; i < sortedUsers.Count; i++)
            {
                int placement = i + 1;
                var userToUpdate = sortedUsers[i];

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
                    CPH.LogInfo($"Updated placement to {placement} for user with User ID {userToUpdate.UserId}.");
                }

                if (userToUpdate.UserId == targetUserId)
                {
                    // Send message with the user's updated placement
                    CPH.SendMessage($"{targetUserName} is now in place {placement} with {userToUpdate.Kills} kills.");
                }
            }
        }
        catch (Exception ex)
        {
            // Log any errors that occur
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

            // Custom PATCH method since HttpMethod.Patch is not available
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

    // Asynchronous method to archive a page in the Notion API (instead of deleting)
    public static async Task<string> NotionArchivePageAsync(string pageId, string apiKey)
    {
        using (var client = new HttpClient())
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            client.DefaultRequestHeaders.Add("Notion-Version", "2022-06-28");

            var url = $"https://api.notion.com/v1/pages/{pageId}";

            // Set "archived" to true instead of trying to delete
            JObject archiveData = new JObject
            {
                ["archived"] = true
            };

            var request = new HttpRequestMessage
            {
                RequestUri = new Uri(url),
                Method = new HttpMethod("PATCH"), // Use PATCH to modify the page properties
                Content = new StringContent(archiveData.ToString(), Encoding.UTF8, "application/json")
            };

            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var responseContent = await response.Content.ReadAsStringAsync();
            return responseContent;
        }
    }
}
