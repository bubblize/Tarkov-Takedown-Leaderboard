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
                // Log the error and stop the script
                CPH.LogError("Error 1002: Could not find a valid user. Exiting script.");
                CPH.SendMessage("Error 1002: Could not find a valid user.");
                return false;
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
                // Log the error and stop the script
                CPH.LogError("Error 1002: Could not find a valid user. Exiting script.");
                CPH.SendMessage("Error 1002: Could not find a valid user.");
                return false;
            }
            CPH.LogInfo($"Retrieved targetUserName: {targetUserName}"); // Log the userName for debugging

            // Retrieve the avatar URL from global variables or arguments
            string avatarUrl = CPH.GetGlobalVar<string>("targetUserProfileImageUrl");
            if (string.IsNullOrEmpty(avatarUrl))
            {
                CPH.TryGetArg("targetUserProfileImageUrl", out avatarUrl);
                if (string.IsNullOrEmpty(avatarUrl))
                {
                    avatarUrl = "https://example.com/default-avatar.png"; // Fallback avatar URL
                }
            }
            CPH.LogInfo($"Using avatarUrl: {avatarUrl}");

            string accessToken = CPH.GetGlobalVar<string>("notionAccessToken");

            // Create a Twitch profile link as the hyperlink for the Name
            string twitchProfileLink = $"https://twitch.tv/{targetUserName}";

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

            // Find the user if they already exist, or create a new entry if they don't
            UserEntry user = users.FirstOrDefault(u => u.UserId == targetUserId);
            if (user != null)
            {
                // User exists, increment their Kills
                user.Kills += 1;
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
            else
            {
                // New user, start with 1 Kill
                user = new UserEntry
                {
                    UserId = targetUserId,
                    Kills = 1,
                    PageId = "" // Will be set after creating a new page
                };

                JObject newPageData = new JObject
                {
                    ["parent"] = new JObject
                    {
                        ["database_id"] = notionDatabaseId
                    },
                    ["properties"] = new JObject
                    {
                        ["Name"] = new JObject
                        {
                            ["title"] = new JArray
                            {
                                new JObject
                                {
                                    ["text"] = new JObject
                                    {
                                        ["content"] = targetUserName,
                                        ["link"] = new JObject
                                        {
                                            ["url"] = twitchProfileLink
                                        }
                                    }
                                }
                            }
                        },
                        ["Avatar"] = new JObject
                        {
                            ["files"] = new JArray
                            {
                                new JObject
                                {
                                    ["name"] = "avatar_image",
                                    ["type"] = "external",
                                    ["external"] = new JObject
                                    {
                                        ["url"] = avatarUrl
                                    }
                                }
                            }
                        },
                        ["Kills"] = new JObject
                        {
                            ["number"] = 1
                        },
                        ["User ID"] = new JObject
                        {
                            ["rich_text"] = new JArray
                            {
                                new JObject
                                {
                                    ["text"] = new JObject
                                    {
                                        ["content"] = targetUserId
                                    }
                                }
                            }
                        },
                        ["Placement"] = new JObject
                        {
                            ["number"] = null // Null to be updated later
                        }
                    }
                };

                // Log the new page data for debugging
                CPH.LogInfo($"Payload for new entry: {newPageData.ToString()}");

                // Create the new page in Notion
                string createResponse = AddToNotionAsync(notionDatabaseId, accessToken, newPageData).GetAwaiter().GetResult();
                CPH.LogInfo($"Notion create response: {createResponse}");
                CPH.SendMessage($"New user entry for {targetUserName} added to Notion with 1 Kill.");

                // Add the new user to the list for placement calculation
                users.Add(user);
            }

            // Sort users by Kills in descending order and assign placements
            var sortedUsers = users.OrderByDescending(u => u.Kills).ToList();
            
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

                if (userToUpdate.UserId == targetUserId)
                {
                    // Send message with the user's updated placement
                    CPH.SendMessage($"{targetUserName} is now in place {placement} with {userToUpdate.Kills} Kills.");
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

    // Asynchronous method to add a page to the Notion API
    public static async Task<string> AddToNotionAsync(string notionDatabaseId, string apiKey, JObject data)
    {
        using (var client = new HttpClient())
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            client.DefaultRequestHeaders.Add("Notion-Version", "2022-06-28");

            var url = $"https://api.notion.com/v1/pages";
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
}
