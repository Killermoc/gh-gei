using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using Octoshift.Models;
using OctoshiftCLI.Extensions;
using Xunit;

namespace OctoshiftCLI.Tests
{
    public class GithubApiTests
    {
        private const string API_URL = "https://api.github.com";
        private readonly RetryPolicy _retryPolicy = new(TestHelpers.CreateMock<OctoLogger>().Object) { _httpRetryInterval = 0, _retryInterval = 0 };
        private readonly Mock<GithubClient> _githubClientMock = TestHelpers.CreateMock<GithubClient>();

        private readonly GithubApi _githubApi;

        private const string GITHUB_ORG = "ORG_LOGIN";
        private const string GITHUB_ENTERPRISE = "ENTERPRISE_NAME";
        private const string GITHUB_REPO = "REPOSITORY_NAME";
        private const string TARGET_ORG = "TARGET_ORG";
        private const string GQL_ERROR_RESPONSE = @"
        {
            ""data"": {
                ""node"":null
            },
            ""errors"":[
                {
                    ""type"":""SERVICE_UNAVAILABLE"",
                    ""path"":[""node""],
                    ""locations"":[{""line"":1,""column"":19}],
                    ""message"":""GitHub Enterprise Importer is currently unavailable. Please try again later.""
                }]
        }";

        public GithubApiTests()
        {
            _githubApi = new GithubApi(_githubClientMock.Object, API_URL, _retryPolicy);
        }

        [Fact]
        public async Task AddAutoLink_Calls_The_Right_Endpoint_With_Payload()
        {
            // Arrange
            const string adoOrg = "ADO_ORG";
            const string adoTeamProject = "ADO_TEAM_PROJECT";

            var keyPrefix = "AB#";
            var urlTemplate = $"https://dev.azure.com/{adoOrg}/{adoTeamProject}/_workitems/edit/<num>/";

            var url = $"https://api.github.com/repos/{GITHUB_ORG}/{GITHUB_REPO}/autolinks";

            var payload = new
            {
                key_prefix = keyPrefix,
                url_template = urlTemplate.Replace(" ", "%20")
            };

            // Act
            await _githubApi.AddAutoLink(GITHUB_ORG, GITHUB_REPO, keyPrefix, urlTemplate);

            // Assert
            _githubClientMock.Verify(m => m.PostAsync(url, It.Is<object>(x => x.ToJson() == payload.ToJson()), null));
        }

        [Fact]
        public async Task AddAutoLink_Replaces_Spaces_In_Url_Template()
        {
            // Arrange
            const string adoOrg = "ADO_ORG";
            const string adoTeamProject = "ADO TEAM PROJECT";

            var keyPrefix = "AB#";
            var urlTemplate = $"https://dev.azure.com/{adoOrg}/{adoTeamProject}/_workitems/edit/<num>/";

            var url = $"https://api.github.com/repos/{GITHUB_ORG}/{GITHUB_REPO}/autolinks";

            var payload = new
            {
                key_prefix = keyPrefix,
                url_template = urlTemplate.Replace(" ", "%20")
            };

            // Act
            await _githubApi.AddAutoLink(GITHUB_ORG, GITHUB_REPO, keyPrefix, urlTemplate);

            // Assert
            _githubClientMock.Verify(m => m.PostAsync(url, It.Is<object>(x => x.ToJson() == payload.ToJson()), null));
        }

        [Fact]
        public async Task GetAutoLinks_Calls_The_Right_Endpoint()
        {
            // Arrange
            var url = $"https://api.github.com/repos/{GITHUB_ORG}/{GITHUB_REPO}/autolinks";

            _githubClientMock.Setup(x => x.GetAllAsync(It.IsAny<string>(), null)).Returns(AsyncEnumerable.Empty<JToken>());

            // Act
            await _githubApi.GetAutoLinks(GITHUB_ORG, GITHUB_REPO);

            // Assert
            _githubClientMock.Verify(m => m.GetAllAsync(url, null));
        }

        [Fact]
        public async Task DeleteAutoLink_Calls_The_Right_Endpoint()
        {
            // Arrange
            const int autoLinkId = 1;

            var url = $"https://api.github.com/repos/{GITHUB_ORG}/{GITHUB_REPO}/autolinks/{autoLinkId}";

            // Act
            await _githubApi.DeleteAutoLink(GITHUB_ORG, GITHUB_REPO, autoLinkId);

            // Assert
            _githubClientMock.Verify(m => m.DeleteAsync(url, null));
        }

        [Fact]
        public async Task CreateTeam_Returns_Created_Team_Id()
        {
            // Arrange
            const string teamName = "TEAM_NAME";

            var url = $"https://api.github.com/orgs/{GITHUB_ORG}/teams";
            var payload = new { name = teamName, privacy = "closed" };

            const string teamId = "TEAM_ID";
            var response = $"{{\"id\": \"{teamId}\"}}";

            _githubClientMock
                .Setup(m => m.PostAsync(url, It.Is<object>(x => x.ToJson() == payload.ToJson()), null))
                .ReturnsAsync(response);

            // Act
            var result = await _githubApi.CreateTeam(GITHUB_ORG, teamName);

            // Assert
            result.Should().Be(teamId);
        }

        [Fact]
        public async Task GetTeams_Returns_All_Teams()
        {
            // Arrange
            var url = $"https://api.github.com/orgs/{GITHUB_ORG}/teams";

            const string team1 = "TEAM_1";
            const string team2 = "TEAM_2";
            const string team3 = "TEAM_3";
            const string team4 = "TEAM_4";

            var teamsResult = new[]
            {
                new { id = 1, name = team1 },
                new { id = 2, name = team2 },
                new { id = 3, name = team3 },
                new { id = 4, name = team4 }
            }.ToAsyncJTokenEnumerable();

            _githubClientMock
                .Setup(m => m.GetAllAsync(url, null))
                .Returns(teamsResult);

            // Act
            var result = (await _githubApi.GetTeams(GITHUB_ORG)).ToArray();

            // Assert
            result.Should().HaveCount(4);
            result.Should().Equal(team1, team2, team3, team4);
        }

        [Fact]
        public async Task GetTeamMembers_Returns_Team_Members()
        {
            // Arrange
            const string teamName = "TEAM_NAME";

            var url = $"https://api.github.com/orgs/{GITHUB_ORG}/teams/{teamName}/members?per_page=100";

            const string teamMember1 = "TEAM_MEMBER_1";
            const string teamMember2 = "TEAM_MEMBER_2";
            var responsePage1 = $@"
            [
                {{
                    ""login"": ""{teamMember1}"",
                    ""id"": 1
                }},
                {{
                    ""login"": ""{teamMember2}"", 
                    ""id"": 2
                }}
            ]";

            const string teamMember3 = "TEAM_MEMBER_3";
            const string teamMember4 = "TEAM_MEMBER_4";
            var responsePage2 = $@"
            [
                {{
                    ""login"": ""{teamMember3}"",
                    ""id"": 3
                }},
                {{
                    ""login"": ""{teamMember4}"", 
                    ""id"": 4
                }}
            ]";

            async IAsyncEnumerable<JToken> GetAllPages()
            {
                var jArrayPage1 = JArray.Parse(responsePage1);
                yield return jArrayPage1[0];
                yield return jArrayPage1[1];

                var jArrayPage2 = JArray.Parse(responsePage2);
                yield return jArrayPage2[0];
                yield return jArrayPage2[1];

                await Task.CompletedTask;
            }

            _githubClientMock
                .Setup(m => m.GetAllAsync(url, null))
                .Returns(GetAllPages);

            // Act
            var result = (await _githubApi.GetTeamMembers(GITHUB_ORG, teamName)).ToArray();

            // Assert
            result.Should().HaveCount(4);
            result.Should().Equal(teamMember1, teamMember2, teamMember3, teamMember4);
        }

        [Fact]
        public async Task GetTeamMembers_Retries_On_404()
        {
            // Arrange
            const string teamName = "TEAM_NAME";

            var url = $"https://api.github.com/orgs/{GITHUB_ORG}/teams/{teamName}/members?per_page=100";

            _githubClientMock
                .SetupSequence(m => m.GetAllAsync(url, null))
                .Throws(new HttpRequestException(null, null, statusCode: HttpStatusCode.NotFound))
                .Throws(new HttpRequestException(null, null, statusCode: HttpStatusCode.NotFound))
                .Returns(new[]
                {
                    new { login = "Sally", id = 1 }
                }.ToAsyncJTokenEnumerable());

            // Act
            var result = (await _githubApi.GetTeamMembers(GITHUB_ORG, teamName)).ToArray();

            // Assert
            result.Should().HaveCount(1);
        }

        [Fact]
        public async Task GetRepos_Returns_Names_Of_All_Repositories()
        {
            // Arrange
            var url = $"https://api.github.com/orgs/{GITHUB_ORG}/repos?per_page=100";

            const string repoName1 = "FOO";
            const string repoName2 = "BAR";
            var responsePage1 = $@"
            [
                {{
                    ""id"": 1,
                    ""name"": ""{repoName1}""
                }},
                {{
                    ""id"": 2,
                    ""name"": ""{repoName2}""
                }}
            ]";

            const string repoName3 = "BAZ";
            const string repoName4 = "QUX";
            var responsePage2 = $@"
            [
                {{
                    ""id"": 3,
                    ""name"": ""{repoName3}""
                }},
                {{
                    ""id"": 4,
                    ""name"": ""{repoName4}""
                }}
            ]";

            async IAsyncEnumerable<JToken> GetAllPages()
            {
                var jArrayPage1 = JArray.Parse(responsePage1);
                yield return jArrayPage1[0];
                yield return jArrayPage1[1];

                var jArrayPage2 = JArray.Parse(responsePage2);
                yield return jArrayPage2[0];
                yield return jArrayPage2[1];

                await Task.CompletedTask;
            }

            _githubClientMock
                .Setup(m => m.GetAllAsync(url, null))
                .Returns(GetAllPages);

            // Act
            var result = (await _githubApi.GetRepos(GITHUB_ORG)).ToArray();

            // Assert
            result.Should().HaveCount(4);
            result.Should().Equal(repoName1, repoName2, repoName3, repoName4);
        }

        [Fact]
        public async Task DoesRepoExist_Returns_True_When_200()
        {
            // Arrange
            var url = $"https://api.github.com/repos/{GITHUB_ORG}/{GITHUB_REPO}";

            _githubClientMock.Setup(m => m.GetNonSuccessAsync(url, HttpStatusCode.NotFound)).ThrowsAsync(new HttpRequestException(null, null, HttpStatusCode.OK));

            // Act
            var result = await _githubApi.DoesRepoExist(GITHUB_ORG, GITHUB_REPO);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public async Task DoesRepoExist_Returns_False_When_404()
        {
            // Arrange
            var url = $"https://api.github.com/repos/{GITHUB_ORG}/{GITHUB_REPO}";

            _githubClientMock.Setup(m => m.GetNonSuccessAsync(url, HttpStatusCode.NotFound)).ReturnsAsync("Not Found");

            // Act
            var result = await _githubApi.DoesRepoExist(GITHUB_ORG, GITHUB_REPO);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public async Task DoesRepoExist_Throws_On_Unexpected_Response()
        {
            // Arrange
            var url = $"https://api.github.com/repos/{GITHUB_ORG}/{GITHUB_REPO}";

            _githubClientMock.Setup(m => m.GetNonSuccessAsync(url, HttpStatusCode.NotFound)).ThrowsAsync(new HttpRequestException(null, null, HttpStatusCode.Unauthorized));

            // Act
            await FluentActions
            .Invoking(async () => await _githubApi.DoesRepoExist(GITHUB_ORG, GITHUB_REPO))
            .Should()
            .ThrowExactlyAsync<HttpRequestException>();
        }

        [Fact]
        public async Task DoesTargetOrgExist_Returns_True_When_200()
        {
            // Arrange
            var url = $"https://api.github.com/orgs/{GITHUB_ORG}";

            _githubClientMock.Setup(m => m.GetNonSuccessAsync(url, HttpStatusCode.NotFound)).ThrowsAsync(new HttpRequestException(null, null, HttpStatusCode.OK));

            // Act
            var result = await _githubApi.DoesOrgExist(GITHUB_ORG);

            // Assert
            result.Should().BeTrue();
        }

        [Fact]
        public async Task DoesTargetOrgExist_Returns_False_When_404()
        {
            // Arrange
            var url = $"https://api.github.com/orgs/{GITHUB_ORG}";

            _githubClientMock.Setup(m => m.GetNonSuccessAsync(url, HttpStatusCode.NotFound)).ReturnsAsync("Not Found");

            // Act
            var result = await _githubApi.DoesOrgExist(GITHUB_ORG);

            // Assert
            result.Should().BeFalse();
        }

        [Fact]
        public async Task DoesTargetOrgExist_Throws_On_Unexpected_Response()
        {
            // Arrange
            var url = $"https://api.github.com/orgs/{GITHUB_ORG}";

            _githubClientMock.Setup(m => m.GetNonSuccessAsync(url, HttpStatusCode.NotFound)).ThrowsAsync(new HttpRequestException(null, null, HttpStatusCode.Unauthorized));

            // Act
            await FluentActions
            .Invoking(async () => await _githubApi.DoesOrgExist(GITHUB_ORG))
            .Should()
            .ThrowExactlyAsync<HttpRequestException>();
        }

        [Fact]
        public async Task RemoveTeamMember_Calls_The_Right_Endpoint()
        {
            // Arrange
            const string teamName = "TEAM_NAME";
            const string member = "MEMBER";

            var url = $"https://api.github.com/orgs/{GITHUB_ORG}/teams/{teamName}/memberships/{member}";

            _githubClientMock.Setup(m => m.DeleteAsync(url, null));

            // Act
            var githubApi = new GithubApi(_githubClientMock.Object, API_URL, _retryPolicy);
            await githubApi.RemoveTeamMember(GITHUB_ORG, teamName, member);

            // Assert
            _githubClientMock.Verify(m => m.DeleteAsync(url, null));
        }

        [Fact]
        public async Task RemoveTeamMember_Retries_On_Exception()
        {
            // Arrange
            const string teamName = "TEAM_NAME";
            const string member = "MEMBER";

            var url = $"https://api.github.com/orgs/{GITHUB_ORG}/teams/{teamName}/memberships/{member}";

            _githubClientMock.SetupSequence(m => m.DeleteAsync(url, null))
                             .ThrowsAsync(new HttpRequestException(null, null, HttpStatusCode.BadGateway))
                             .ReturnsAsync(string.Empty);

            // Act
            var githubApi = new GithubApi(_githubClientMock.Object, API_URL, _retryPolicy);
            await githubApi.RemoveTeamMember(GITHUB_ORG, teamName, member);

            // Assert
            _githubClientMock.Verify(m => m.DeleteAsync(url, null), Times.Exactly(2));
        }

        [Fact]
        public async Task AddTeamSync_Calls_The_Right_Endpoint_With_Payload()
        {
            // Arrange
            const string teamName = "TEAM_NAME";
            const string groupId = "GROUP_ID";
            const string groupName = "GROUP_NAME";
            const string groupDesc = "GROUP_DESC";

            var url = $"https://api.github.com/orgs/{GITHUB_ORG}/teams/{teamName}/team-sync/group-mappings";
            var payload = new
            {
                groups = new[] { new { group_id = groupId, group_name = groupName, group_description = groupDesc } }
            };

            // Act
            await _githubApi.AddTeamSync(GITHUB_ORG, teamName, groupId, groupName, groupDesc);

            // Assert
            _githubClientMock.Verify(m => m.PatchAsync(url, It.Is<object>(x => x.ToJson() == payload.ToJson()), null));
        }

        [Fact]
        public async Task AddTeamToRepo_Calls_The_Right_Endpoint_With_Payload()
        {
            // Arrange
            const string teamName = "TEAM_NAME";
            const string role = "ROLE";

            var url = $"https://api.github.com/orgs/{GITHUB_ORG}/teams/{teamName}/repos/{GITHUB_ORG}/{GITHUB_REPO}";
            var payload = new { permission = role };

            // Act
            await _githubApi.AddTeamToRepo(GITHUB_ORG, GITHUB_REPO, teamName, role);

            // Assert
            _githubClientMock.Verify(m => m.PutAsync(url, It.Is<object>(x => x.ToJson() == payload.ToJson()), null));
        }

        [Fact]
        public async Task GetOrganizationId_Returns_The_Org_Id()
        {
            // Arrange
            const string orgId = "ORG_ID";

            var url = $"https://api.github.com/graphql";
            var payload =
                $"{{\"query\":\"query($login: String!) {{organization(login: $login) {{ login, id, name }} }}\",\"variables\":{{\"login\":\"{GITHUB_ORG}\"}}}}";
            var response = $@"
            {{
                ""data"": 
                    {{
                        ""organization"": 
                            {{
                                ""login"": ""{GITHUB_ORG}"",
                                ""id"": ""{orgId}"",
                                ""name"": ""github"" 
                            }} 
                    }} 
            }}";

            _githubClientMock
                .Setup(m => m.PostAsync(url, It.Is<object>(x => x.ToJson() == payload), null))
                .ReturnsAsync(response);

            // Act
            var result = await _githubApi.GetOrganizationId(GITHUB_ORG);

            // Assert
            result.Should().Be(orgId);
        }

        [Fact]
        public async Task GetOrganizationId_Retries_On_GQL_Error()
        {
            // Arrange
            const string orgId = "ORG_ID";

            var url = $"https://api.github.com/graphql";
            var payload =
                $"{{\"query\":\"query($login: String!) {{organization(login: $login) {{ login, id, name }} }}\",\"variables\":{{\"login\":\"{GITHUB_ORG}\"}}}}";

            var response = $@"
            {{
                ""data"": 
                    {{
                        ""organization"": 
                            {{
                                ""login"": ""{GITHUB_ORG}"",
                                ""id"": ""{orgId}"",
                                ""name"": ""github"" 
                            }} 
                    }} 
            }}";

            _githubClientMock
                .SetupSequence(m => m.PostAsync(url, It.Is<object>(x => x.ToJson() == payload), null))
                .ReturnsAsync(GQL_ERROR_RESPONSE)
                .ReturnsAsync(GQL_ERROR_RESPONSE)
                .ReturnsAsync(response);

            // Act
            var result = await _githubApi.GetOrganizationId(GITHUB_ORG);

            // Assert
            result.Should().Be(orgId);
        }

        [Fact]
        public async Task GetEnterpriseId_Returns_The_Enterprise_Id()
        {
            // Arrange
            const string enterpriseId = "ENTERPRISE_ID";

            var url = $"https://api.github.com/graphql";
            var payload =
                $"{{\"query\":\"query($slug: String!) {{enterprise (slug: $slug) {{ slug, id }} }}\",\"variables\":{{\"slug\":\"{GITHUB_ENTERPRISE}\"}}}}";
            var response = $@"
            {{
                ""data"": 
                    {{
                        ""enterprise"": 
                            {{
                                ""slug"": ""{GITHUB_ENTERPRISE}"",
                                ""id"": ""{enterpriseId}""
                            }} 
                    }} 
            }}";

            _githubClientMock
                .Setup(m => m.PostAsync(url, It.Is<object>(x => x.ToJson() == payload), null))
                .ReturnsAsync(response);

            // Act
            var result = await _githubApi.GetEnterpriseId(GITHUB_ENTERPRISE);

            // Assert
            result.Should().Be(enterpriseId);
        }

        [Fact]
        public async Task GetEnterpriseId_Retries_On_GQL_Error()
        {
            // Arrange
            const string enterpriseId = "ENTERPRISE_ID";

            var url = $"https://api.github.com/graphql";
            var payload =
                $"{{\"query\":\"query($slug: String!) {{enterprise (slug: $slug) {{ slug, id }} }}\",\"variables\":{{\"slug\":\"{GITHUB_ENTERPRISE}\"}}}}";

            var response = $@"
            {{
                ""data"": 
                    {{
                        ""enterprise"": 
                            {{
                                ""slug"": ""{GITHUB_ENTERPRISE}"",
                                ""id"": ""{enterpriseId}""
                            }} 
                    }} 
            }}";

            _githubClientMock
                .SetupSequence(m => m.PostAsync(url, It.Is<object>(x => x.ToJson() == payload), null))
                .ReturnsAsync(GQL_ERROR_RESPONSE)
                .ReturnsAsync(GQL_ERROR_RESPONSE)
                .ReturnsAsync(response);

            // Act
            var result = await _githubApi.GetEnterpriseId(GITHUB_ENTERPRISE);

            // Assert
            result.Should().Be(enterpriseId);
        }

        [Fact]
        public async Task CreateAdoMigrationSource_Returns_New_Migration_Source_Id()
        {
            // Arrange
            const string url = "https://api.github.com/graphql";
            const string orgId = "ORG_ID";
            var payload =
                "{\"query\":\"mutation createMigrationSource($name: String!, $url: String!, $ownerId: ID!, $type: MigrationSourceType!) " +
                "{ createMigrationSource(input: {name: $name, url: $url, ownerId: $ownerId, type: $type}) { migrationSource { id, name, url, type } } }\"" +
                $",\"variables\":{{\"name\":\"Azure DevOps Source\",\"url\":\"https://dev.azure.com\",\"ownerId\":\"{orgId}\",\"type\":\"AZURE_DEVOPS\"}},\"operationName\":\"createMigrationSource\"}}";
            const string actualMigrationSourceId = "MS_kgC4NjFhOTVjOTc4ZTRhZjEwMDA5NjNhOTdm";
            var response = $@"
            {{
                ""data"": {{
                    ""createMigrationSource"": {{
                        ""migrationSource"": {{
                            ""id"": ""{actualMigrationSourceId}"",
                            ""name"": ""Azure Devops Source"",
                            ""url"": ""https://dev.azure.com"",
                            ""type"": ""AZURE_DEVOPS""
                        }}
                    }}
                }}
            }}";

            _githubClientMock
                .Setup(m => m.PostAsync(url, It.Is<object>(x => x.ToJson() == payload), null))
                .ReturnsAsync(response);

            // Act
            var expectedMigrationSourceId = await _githubApi.CreateAdoMigrationSource(orgId, null);

            // Assert
            expectedMigrationSourceId.Should().Be(actualMigrationSourceId);
        }

        [Fact]
        public async Task CreateAdoMigrationSource_Uses_Ado_Server_Url()
        {
            // Arrange
            const string url = "https://api.github.com/graphql";
            const string orgId = "ORG_ID";
            const string adoServerUrl = "https://ado.contoso.com";
            var payload =
                "{\"query\":\"mutation createMigrationSource($name: String!, $url: String!, $ownerId: ID!, $type: MigrationSourceType!) " +
                "{ createMigrationSource(input: {name: $name, url: $url, ownerId: $ownerId, type: $type}) { migrationSource { id, name, url, type } } }\"" +
                $",\"variables\":{{\"name\":\"Azure DevOps Source\",\"url\":\"{adoServerUrl}\",\"ownerId\":\"{orgId}\",\"type\":\"AZURE_DEVOPS\"}},\"operationName\":\"createMigrationSource\"}}";
            const string actualMigrationSourceId = "MS_kgC4NjFhOTVjOTc4ZTRhZjEwMDA5NjNhOTdm";
            var response = $@"
            {{
                ""data"": {{
                    ""createMigrationSource"": {{
                        ""migrationSource"": {{
                            ""id"": ""{actualMigrationSourceId}"",
                            ""name"": ""Azure Devops Source"",
                            ""url"": ""{adoServerUrl}"",
                            ""type"": ""AZURE_DEVOPS""
                        }}
                    }}
                }}
            }}";

            _githubClientMock
                .Setup(m => m.PostAsync(url, It.Is<object>(x => x.ToJson() == payload), null))
                .ReturnsAsync(response);

            // Act
            var expectedMigrationSourceId = await _githubApi.CreateAdoMigrationSource(orgId, adoServerUrl);

            // Assert
            expectedMigrationSourceId.Should().Be(actualMigrationSourceId);
        }

        [Fact]
        public async Task CreateBbsMigrationSource_Returns_New_Migration_Source_Id()
        {
            // Arrange
            const string url = "https://api.github.com/graphql";
            const string orgId = "ORG_ID";
            var payload =
                "{\"query\":\"mutation createMigrationSource($name: String!, $url: String!, $ownerId: ID!, $type: MigrationSourceType!) " +
                "{ createMigrationSource(input: {name: $name, url: $url, ownerId: $ownerId, type: $type}) { migrationSource { id, name, url, type } } }\"" +
                $",\"variables\":{{\"name\":\"Bitbucket Server Source\",\"url\":\"https://not-used\",\"ownerId\":\"{orgId}\",\"type\":\"BITBUCKET_SERVER\"}},\"operationName\":\"createMigrationSource\"}}";
            const string actualMigrationSourceId = "MS_kgC4NjFhOTVjOTc4ZTRhZjEwMDA5NjNhOTdm";
            var response = $@"
            {{
                ""data"": {{
                    ""createMigrationSource"": {{
                        ""migrationSource"": {{
                            ""id"": ""{actualMigrationSourceId}"",
                            ""name"": ""Bitbucket Server Source"",
                            ""url"": ""https://not-used"",
                            ""type"": ""BITBUCKET_SERVER""
                        }}
                    }}
                }}
            }}";

            _githubClientMock
                .Setup(m => m.PostAsync(url, It.Is<object>(x => x.ToJson() == payload), null))
                .ReturnsAsync(response);

            // Act
            var expectedMigrationSourceId = await _githubApi.CreateBbsMigrationSource(orgId);

            // Assert
            expectedMigrationSourceId.Should().Be(actualMigrationSourceId);
        }

        [Fact]
        public async Task CreateGhecMigrationSource_Returns_New_Migration_Source_Id()
        {
            // Arrange
            const string url = "https://api.github.com/graphql";
            const string orgId = "ORG_ID";
            var payload =
                "{\"query\":\"mutation createMigrationSource($name: String!, $url: String!, $ownerId: ID!, $type: MigrationSourceType!) " +
                "{ createMigrationSource(input: {name: $name, url: $url, ownerId: $ownerId, type: $type}) { migrationSource { id, name, url, type } } }\"" +
                $",\"variables\":{{\"name\":\"GHEC Source\",\"url\":\"https://github.com\",\"ownerId\":\"{orgId}\",\"type\":\"GITHUB_ARCHIVE\"}},\"operationName\":\"createMigrationSource\"}}";
            const string actualMigrationSourceId = "MS_kgC4NjFhOTVjOTc4ZTRhZjEwMDA5NjNhOTdm";
            var response = $@"
            {{
                ""data"": {{
                    ""createMigrationSource"": {{
                        ""migrationSource"": {{
                            ""id"": ""{actualMigrationSourceId}"",
                            ""name"": ""GHEC Source"",
                            ""url"": ""https://github.com"",
                            ""type"": ""GITHUB_ARCHIVE""
                        }}
                    }}
                }}
            }}";

            _githubClientMock
                .Setup(m => m.PostAsync(url, It.Is<object>(x => x.ToJson() == payload), null))
                .ReturnsAsync(response);

            // Act
            var expectedMigrationSourceId = await _githubApi.CreateGhecMigrationSource(orgId);

            // Assert
            expectedMigrationSourceId.Should().Be(actualMigrationSourceId);
        }

        [Fact]
        public async Task StartMigration_Returns_New_Repository_Migration_Id()
        {
            // Arrange
            const string migrationSourceId = "MIGRATION_SOURCE_ID";
            const string adoRepoUrl = "ADO_REPO_URL";
            const string orgId = "ORG_ID";
            const string url = "https://api.github.com/graphql";
            const string gitArchiveUrl = "GIT_ARCHIVE_URL";
            const string metadataArchiveUrl = "METADATA_ARCHIVE_URL";
            const string sourceToken = "SOURCE_TOKEN";
            const string targetToken = "TARGET_TOKEN";

            const string query = @"
                mutation startRepositoryMigration(
                    $sourceId: ID!,
                    $ownerId: ID!,
                    $sourceRepositoryUrl: URI!,
                    $repositoryName: String!,
                    $continueOnError: Boolean!,
                    $gitArchiveUrl: String,
                    $metadataArchiveUrl: String,
                    $accessToken: String!,
                    $githubPat: String,
                    $skipReleases: Boolean,
                    $lockSource: Boolean)";
            const string gql = @"
                startRepositoryMigration(
                    input: { 
                        sourceId: $sourceId,
                        ownerId: $ownerId,
                        sourceRepositoryUrl: $sourceRepositoryUrl,
                        repositoryName: $repositoryName,
                        continueOnError: $continueOnError,
                        gitArchiveUrl: $gitArchiveUrl,
                        metadataArchiveUrl: $metadataArchiveUrl,
                        accessToken: $accessToken,
                        githubPat: $githubPat,
                        skipReleases: $skipReleases,
                        lockSource: $lockSource
                    }
                ) {
                    repositoryMigration {
                        id,
                        migrationSource {
                            id,
                            name,
                            type
                        },
                        sourceUrl,
                        state,
                        failureReason
                    }
                  }";
            var payload = new
            {
                query = $"{query} {{ {gql} }}",
                variables = new
                {
                    sourceId = migrationSourceId,
                    ownerId = orgId,
                    sourceRepositoryUrl = adoRepoUrl,
                    repositoryName = GITHUB_REPO,
                    continueOnError = true,
                    gitArchiveUrl,
                    metadataArchiveUrl,
                    accessToken = sourceToken,
                    githubPat = targetToken,
                    skipReleases = false,
                    lockSource = false
                },
                operationName = "startRepositoryMigration"
            };
            const string actualRepositoryMigrationId = "RM_kgC4NjFhNmE2NGU2ZWE1YTQwMDA5ODliZjhi";
            var response = $@"
            {{
                ""data"": {{
                    ""startRepositoryMigration"": {{
                        ""repositoryMigration"": {{
                            ""id"": ""{actualRepositoryMigrationId}"",
                            ""migrationSource"": {{
                                ""id"": ""MS_kgC4NjFhNmE2NDViNWZmOTEwMDA5MTZiMGQw"",
                                ""name"": ""Azure Devops Source"",
                                ""type"": ""AZURE_DEVOPS""
                            }},
                        ""sourceUrl"": ""https://dev.azure.com/github-inside-msft/Team-Demos/_git/Tiny"",
                        ""state"": ""QUEUED"",
                        ""failureReason"": """"
                        }}
                    }}
                }}
            }}";

            _githubClientMock
                .Setup(m => m.PostAsync(url, It.Is<object>(x => x.ToJson() == payload.ToJson()), null))
                .ReturnsAsync(response);

            // Act
            var expectedRepositoryMigrationId = await _githubApi.StartMigration(migrationSourceId, adoRepoUrl, orgId, GITHUB_REPO, sourceToken, targetToken, gitArchiveUrl, metadataArchiveUrl);

            // Assert
            expectedRepositoryMigrationId.Should().Be(actualRepositoryMigrationId);
        }

        [Fact]
        public async Task StartBbsMigration_Returns_New_Repository_Migration_Id()
        {
            // Arrange
            const string migrationSourceId = "MIGRATION_SOURCE_ID";
            const string orgId = "ORG_ID";
            const string url = "https://api.github.com/graphql";
            const string gitArchiveUrl = "GIT_ARCHIVE_URL";
            const string targetToken = "TARGET_TOKEN";

            const string unusedSourceRepoUrl = "https://not-used";
            const string unusedSourceToken = "not-used";
            const string unusedMetadataArchiveUrl = "https://not-used";

            const string query = @"
                mutation startRepositoryMigration(
                    $sourceId: ID!,
                    $ownerId: ID!,
                    $sourceRepositoryUrl: URI!,
                    $repositoryName: String!,
                    $continueOnError: Boolean!,
                    $gitArchiveUrl: String,
                    $metadataArchiveUrl: String,
                    $accessToken: String!,
                    $githubPat: String,
                    $skipReleases: Boolean,
                    $lockSource: Boolean)";
            const string gql = @"
                startRepositoryMigration(
                    input: { 
                        sourceId: $sourceId,
                        ownerId: $ownerId,
                        sourceRepositoryUrl: $sourceRepositoryUrl,
                        repositoryName: $repositoryName,
                        continueOnError: $continueOnError,
                        gitArchiveUrl: $gitArchiveUrl,
                        metadataArchiveUrl: $metadataArchiveUrl,
                        accessToken: $accessToken,
                        githubPat: $githubPat,
                        skipReleases: $skipReleases,
                        lockSource: $lockSource
                    }
                ) {
                    repositoryMigration {
                        id,
                        migrationSource {
                            id,
                            name,
                            type
                        },
                        sourceUrl,
                        state,
                        failureReason
                    }
                  }";
            var payload = new
            {
                query = $"{query} {{ {gql} }}",
                variables = new
                {
                    sourceId = migrationSourceId,
                    ownerId = orgId,
                    sourceRepositoryUrl = unusedSourceRepoUrl,
                    repositoryName = GITHUB_REPO,
                    continueOnError = true,
                    gitArchiveUrl,
                    metadataArchiveUrl = unusedMetadataArchiveUrl,
                    accessToken = unusedSourceToken,
                    githubPat = targetToken,
                    skipReleases = false,
                    lockSource = false
                },
                operationName = "startRepositoryMigration"
            };
            const string actualRepositoryMigrationId = "RM_kgC4NjFhNmE2NGU2ZWE1YTQwMDA5ODliZjhi";
            var response = $@"
            {{
                ""data"": {{
                    ""startRepositoryMigration"": {{
                        ""repositoryMigration"": {{
                            ""id"": ""{actualRepositoryMigrationId}"",
                            ""migrationSource"": {{
                                ""id"": ""MS_kgC4NjFhNmE2NDViNWZmOTEwMDA5MTZiMGQw"",
                                ""name"": ""Azure Devops Source"",
                                ""type"": ""AZURE_DEVOPS""
                            }},
                        ""sourceUrl"": ""https://dev.azure.com/github-inside-msft/Team-Demos/_git/Tiny"",
                        ""state"": ""QUEUED"",
                        ""failureReason"": """"
                        }}
                    }}
                }}
            }}";

            _githubClientMock
                .Setup(m => m.PostAsync(url, It.Is<object>(x => x.ToJson() == payload.ToJson()), null))
                .ReturnsAsync(response);

            // Act
            var expectedRepositoryMigrationId = await _githubApi.StartBbsMigration(migrationSourceId, orgId, GITHUB_REPO, targetToken, gitArchiveUrl);

            // Assert
            expectedRepositoryMigrationId.Should().Be(actualRepositoryMigrationId);
        }

        [Fact]
        public async Task StartMigration_Throws_When_GraphQL_Response_Has_Errors()
        {
            // Arrange
            const string expectedErrorMessage = "Please make sure that githubPat includes the workflow scope";
            const string response = $@"
            {{
                ""data"": {{ 
                    ""startRepositoryMigration"": null
                 }},
                ""errors"": [
                     {{
                        ""type"": ""FORBIDDEN"",
                        ""path"": [
                            ""startRepositoryMigration""
                         ],
                        ""locations"": [
                            {{
                                ""line"": 13,
                                ""column"": 17
                            }}
                         ],
                        ""message"": ""{expectedErrorMessage}""
                     }}
                 ]
            }}";

            _githubClientMock
                .Setup(m => m.PostAsync(It.IsAny<string>(), It.IsAny<object>(), null))
                .ReturnsAsync(response);

            // Act, Assert
            await _githubApi.Invoking(api => api.StartMigration(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()))
                .Should()
                .ThrowAsync<OctoshiftCliException>()
                .WithMessage(expectedErrorMessage);
        }

        [Fact]
        public async Task StartMigration_Does_Not_Include_Error_Message_If_Missing()
        {
            // Arrange
            const string response = @"
            {
                ""data"": { 
                    ""startRepositoryMigration"": null
                 },
                ""errors"": [
                     {
                        ""type"": ""FORBIDDEN"",
                        ""path"": [
                            ""startRepositoryMigration""
                         ],
                        ""locations"": [
                            {
                                ""line"": 13,
                                ""column"": 17
                            }
                         ]
                     }
                 ]
            }";

            const string expectedErrorMessage = "UNKNOWN";

            _githubClientMock
                .Setup(m => m.PostAsync(It.IsAny<string>(), It.IsAny<object>(), null))
                .ReturnsAsync(response);

            // Act, Assert
            await _githubApi.Invoking(api => api.StartMigration(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()))
                .Should()
                .ThrowAsync<OctoshiftCliException>()
                .WithMessage(expectedErrorMessage);
        }

        [Fact]
        public async Task StartMigration_Does_Not_Throw_When_Errors_Is_Empty()
        {
            // Arrange
            const string response = @"
            {
                ""data"": { 
                    ""startRepositoryMigration"": {
                        ""repositoryMigration"": {
                            ""id"": ""RM_kgC4NjFhNmE2NGU2ZWE1YTQwMDA5ODliZjhi""
                         }
                     }
                 },
                ""errors"": []
            }";

            _githubClientMock
                .Setup(m => m.PostAsync(It.IsAny<string>(), It.IsAny<object>(), null))
                .ReturnsAsync(response);

            // Act, Assert
            await _githubApi.Invoking(api => api.StartMigration(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()))
                .Should()
                .NotThrowAsync<OctoshiftCliException>();
        }

        [Fact]
        public async Task GetMigration_Returns_The_Migration_State_And_Repository_Name()
        {
            // Arrange
            const string migrationId = "MIGRATION_ID";
            const string url = "https://api.github.com/graphql";

            var payload =
                "{\"query\":\"query($id: ID!) { node(id: $id) { ... on Migration { id, sourceUrl, migrationSource { name }, state, failureReason, repositoryName } } }\"" +
                $",\"variables\":{{\"id\":\"{migrationId}\"}}}}";
            const string actualMigrationState = "SUCCEEDED";
            var response = $@"
            {{
                ""data"": {{
                    ""node"": {{
                        ""id"": ""RM_kgC4NjFhNmE2ZWY1NmE4MjAwMDA4NjA5NTZi"",
                        ""sourceUrl"": ""https://github.com/import-testing/archive-export-testing"",
                        ""migrationSource"": {{
                            ""name"": ""GHEC Archive Source""
                        }},
                        ""state"": ""{actualMigrationState}"",
                        ""failureReason"": """",
                        ""repositoryName"": ""{GITHUB_REPO}""
                    }}
                }}
            }}";

            _githubClientMock
                .Setup(m => m.PostAsync(url, It.Is<object>(x => x.ToJson() == payload), null))
                .ReturnsAsync(response);

            // Act
            var (expectedMigrationState, expectedRepositoryName, expectedFailureReason) = await _githubApi.GetMigration(migrationId);

            // Assert
            expectedMigrationState.Should().Be(actualMigrationState);
            expectedRepositoryName.Should().Be(GITHUB_REPO);
            expectedFailureReason.Should().BeEmpty();
        }

        [Fact]
        public async Task GetMigration_Retries_On_Http_502()
        {
            // Arrange
            const string migrationId = "MIGRATION_ID";
            const string url = "https://api.github.com/graphql";

            var payload =
                "{\"query\":\"query($id: ID!) { node(id: $id) { ... on Migration { id, sourceUrl, migrationSource { name }, state, failureReason, repositoryName } } }\"" +
                $",\"variables\":{{\"id\":\"{migrationId}\"}}}}";
            const string actualMigrationState = "SUCCEEDED";
            var response = $@"
            {{
                ""data"": {{
                    ""node"": {{
                        ""id"": ""RM_kgC4NjFhNmE2ZWY1NmE4MjAwMDA4NjA5NTZi"",
                        ""sourceUrl"": ""https://github.com/import-testing/archive-export-testing"",
                        ""migrationSource"": {{
                            ""name"": ""GHEC Archive Source""
                        }},
                        ""state"": ""{actualMigrationState}"",
                        ""failureReason"": """"
                    }}
                }}
            }}";

            _githubClientMock
                .SetupSequence(m => m.PostAsync(url, It.Is<object>(x => x.ToJson() == payload), null))
                .Throws(new HttpRequestException(null, null, statusCode: HttpStatusCode.BadGateway))
                .Throws(new HttpRequestException(null, null, statusCode: HttpStatusCode.BadGateway))
                .ReturnsAsync(response);

            // Act
            var (expectedMigrationState, _, _) = await _githubApi.GetMigration(migrationId);

            // Assert
            expectedMigrationState.Should().Be(actualMigrationState);
        }

        [Fact]
        public async Task GetMigration_Retries_On_GQL_Error()
        {
            // Arrange
            const string migrationId = "MIGRATION_ID";
            const string url = "https://api.github.com/graphql";

            var payload =
                "{\"query\":\"query($id: ID!) { node(id: $id) { ... on Migration { id, sourceUrl, migrationSource { name }, state, failureReason, repositoryName } } }\"" +
                $",\"variables\":{{\"id\":\"{migrationId}\"}}}}";
            const string actualMigrationState = "SUCCEEDED";

            var response = $@"
            {{
                ""data"": {{
                    ""node"": {{
                        ""id"": ""RM_kgC4NjFhNmE2ZWY1NmE4MjAwMDA4NjA5NTZi"",
                        ""sourceUrl"": ""https://github.com/import-testing/archive-export-testing"",
                        ""migrationSource"": {{
                            ""name"": ""GHEC Archive Source""
                        }},
                        ""state"": ""{actualMigrationState}"",
                        ""failureReason"": """"
                    }}
                }}
            }}";

            _githubClientMock
                .SetupSequence(m => m.PostAsync(url, It.Is<object>(x => x.ToJson() == payload), null))
                .ReturnsAsync(GQL_ERROR_RESPONSE)
                .ReturnsAsync(GQL_ERROR_RESPONSE)
                .ReturnsAsync(response);

            // Act
            var (expectedMigrationState, _, _) = await _githubApi.GetMigration(migrationId);

            // Assert
            expectedMigrationState.Should().Be(actualMigrationState);
        }

        [Fact]
        public async Task GetMigration_Returns_The_Migration_Failure_Reason()
        {
            // Arrange
            const string migrationId = "MIGRATION_ID";
            const string url = "https://api.github.com/graphql";

            var payload =
                "{\"query\":\"query($id: ID!) { node(id: $id) { ... on Migration { id, sourceUrl, migrationSource { name }, state, failureReason, repositoryName } } }\"" +
                $",\"variables\":{{\"id\":\"{migrationId}\"}}}}";
            const string actualFailureReason = "FAILURE_REASON";
            var response = $@"
            {{
                ""data"": {{
                    ""node"": {{
                        ""id"": ""RM_kgC4NjFhNmE2ZWY1NmE4MjAwMDA4NjA5NTZi"",
                        ""sourceUrl"": ""https://github.com/import-testing/archive-export-testing"",
                        ""migrationSource"": {{
                            ""name"": ""GHEC Archive Source""
                        }},
                        ""state"": ""FAILED"",
                        ""failureReason"": ""{actualFailureReason}"",
                        ""repositoryName"": ""{GITHUB_REPO}""
                    }}
                }}
            }}";

            _githubClientMock
                .Setup(m => m.PostAsync(url, It.Is<object>(x => x.ToJson() == payload), null))
                .ReturnsAsync(response);

            // Act
            var (_, _, expectedFailureReason) = await _githubApi.GetMigration(migrationId);

            // Assert
            expectedFailureReason.Should().Be(actualFailureReason);
        }

        [Fact]
        public async Task GetOrganizationMigration_Returns_The_Migration_State_And_Org_URL_And_Name_And_Progress_Information()
        {
            // Arrange
            const string migrationId = "MIGRATION_ID";
            const string url = "https://api.github.com/graphql";
            const string sourceOrgUrl = "https://github.com/import-testing";

            var payload =
                "{\"query\":\"query($id: ID!) { node(id: $id) { ... on OrganizationMigration { state, sourceOrgUrl, targetOrgName, failureReason, remainingRepositoriesCount, totalRepositoriesCount } } }\"" +
                $",\"variables\":{{\"id\":\"{migrationId}\"}}}}";
            const string actualMigrationState = "SUCCEEDED";
            const int actualRemainingRepositoriesCount = 0;
            const int actualTotalRepositoriesCount = 9000;
            var response = $@"
            {{
                ""data"": {{
                    ""node"": {{
                        ""sourceOrgUrl"": ""{sourceOrgUrl}"",
                        ""state"": ""{actualMigrationState}"",
                        ""failureReason"": """",
                        ""targetOrgName"": ""{TARGET_ORG}"",
                        ""remainingRepositoriesCount"": {actualRemainingRepositoriesCount},
                        ""totalRepositoriesCount"": {actualTotalRepositoriesCount}
                    }}
                }}
            }}";

            _githubClientMock
                .Setup(m => m.PostAsync(url, It.Is<object>(x => x.ToJson() == payload), null))
                .ReturnsAsync(response);

            // Act
            var (expectedMigrationState, expectedSourceOrgUrl, expectedTargetOrgName, expectedFailureReason, expectedRemainingRepositoriesCount, expectedTotalRepositoriesCount) = await _githubApi.GetOrganizationMigration(migrationId);

            // Assert
            expectedMigrationState.Should().Be(actualMigrationState);
            expectedSourceOrgUrl.Should().Be(sourceOrgUrl);
            expectedTargetOrgName.Should().Be(TARGET_ORG);
            expectedFailureReason.Should().BeEmpty();
            expectedRemainingRepositoriesCount.Should().Be(actualRemainingRepositoriesCount);
            expectedTotalRepositoriesCount.Should().Be(actualTotalRepositoriesCount);
        }

        [Fact]
        public async Task GetOrganizationMigration_Retries_On_502()
        {
            // Arrange
            const string migrationId = "MIGRATION_ID";
            const string url = "https://api.github.com/graphql";
            const string sourceOrgUrl = "https://github.com/import-testing";

            var payload =
                "{\"query\":\"query($id: ID!) { node(id: $id) { ... on OrganizationMigration { state, sourceOrgUrl, targetOrgName, failureReason, remainingRepositoriesCount, totalRepositoriesCount } } }\"" +
                $",\"variables\":{{\"id\":\"{migrationId}\"}}}}";
            const string actualMigrationState = "SUCCEEDED";
            const int actualRemainingRepositoriesCount = 0;
            const int actualTotalRepositoriesCount = 9000;
            var response = $@"
            {{
                ""data"": {{
                    ""node"": {{
                        ""sourceOrgUrl"": ""{sourceOrgUrl}"",
                        ""state"": ""{actualMigrationState}"",
                        ""failureReason"": """",
                        ""targetOrgName"": ""{TARGET_ORG}"",
                        ""remainingRepositoriesCount"": {actualRemainingRepositoriesCount},
                        ""totalRepositoriesCount"": {actualTotalRepositoriesCount}
                    }}
                }}
            }}";

            _githubClientMock
                .SetupSequence(m => m.PostAsync(url, It.Is<object>(x => x.ToJson() == payload), null))
                .Throws(new HttpRequestException(null, null, statusCode: HttpStatusCode.BadGateway))
                .Throws(new HttpRequestException(null, null, statusCode: HttpStatusCode.BadGateway))
                .ReturnsAsync(response);

            // Act
            var (expectedMigrationState, _, _, _, _, _) = await _githubApi.GetOrganizationMigration(migrationId);

            // Assert
            expectedMigrationState.Should().Be(actualMigrationState);
        }

        [Fact]
        public async Task GetOrganizationMigration_Retries_On_GQL_Error()
        {
            // Arrange
            const string migrationId = "MIGRATION_ID";
            const string url = "https://api.github.com/graphql";
            const string sourceOrgUrl = "https://github.com/import-testing";

            var payload =
                "{\"query\":\"query($id: ID!) { node(id: $id) { ... on OrganizationMigration { state, sourceOrgUrl, targetOrgName, failureReason, remainingRepositoriesCount, totalRepositoriesCount } } }\"" +
                $",\"variables\":{{\"id\":\"{migrationId}\"}}}}";
            const string actualMigrationState = "SUCCEEDED";
            var response = $@"
            {{
                ""data"": {{
                    ""node"": {{
                        ""sourceOrgUrl"": ""{sourceOrgUrl}"",
                        ""state"": ""{actualMigrationState}"",
                        ""failureReason"": """",
                        ""targetOrgName"": ""{TARGET_ORG}""
                    }}
                }}
            }}";

            _githubClientMock
                .SetupSequence(m => m.PostAsync(url, It.Is<object>(x => x.ToJson() == payload), null))
                .ReturnsAsync(GQL_ERROR_RESPONSE)
                .ReturnsAsync(GQL_ERROR_RESPONSE)
                .ReturnsAsync(response);

            // Act
            var (expectedMigrationState, _, _, _, _, _) = await _githubApi.GetOrganizationMigration(migrationId);

            // Assert
            expectedMigrationState.Should().Be(actualMigrationState);
        }

        [Fact]
        public async Task GetOrganizationMigration_Returns_The_Migration_Failure_Reason()
        {
            // Arrange
            const string migrationId = "MIGRATION_ID";
            const string url = "https://api.github.com/graphql";
            const string sourceOrgUrl = "https://github.com/import-testing";

            var payload =
                "{\"query\":\"query($id: ID!) { node(id: $id) { ... on OrganizationMigration { state, sourceOrgUrl, targetOrgName, failureReason, remainingRepositoriesCount, totalRepositoriesCount } } }\"" +
                $",\"variables\":{{\"id\":\"{migrationId}\"}}}}";
            const string actualFailureReason = "FAILURE_REASON";
            const int actualRemainingRepositoriesCount = 9000;
            const int actualTotalRepositoriesCount = 9000;
            var response = $@"
            {{
                ""data"": {{
                    ""node"": {{
                        ""sourceOrgUrl"": ""{sourceOrgUrl}"",
                        ""state"": ""FAILED"",
                        ""failureReason"": ""{actualFailureReason}"",
                        ""targetOrgName"": ""{TARGET_ORG}"",
                        ""remainingRepositoriesCount"": {actualRemainingRepositoriesCount},
                        ""totalRepositoriesCount"": {actualTotalRepositoriesCount}
                    }}
                }}
            }}";

            _githubClientMock
                .Setup(m => m.PostAsync(url, It.Is<object>(x => x.ToJson() == payload), null))
                .ReturnsAsync(response);

            // Act
            var (_, _, _, expectedFailureReason, _, _) = await _githubApi.GetOrganizationMigration(migrationId);

            // Assert
            expectedFailureReason.Should().Be(actualFailureReason);
        }

        [Fact]
        public async Task GetMigrationLogUrl_Returns_The_Migration_Log_URL()
        {
            // Arrange
            const string url = "https://api.github.com/graphql";

            var query = "query ($org: String!, $repo: String!)";
            var gql = @"
                organization(login: $org) {
                    repositoryMigrations(last: 1, repositoryName: $repo) {
                        nodes {
                            migrationLogUrl
                        }
                    }
                }
            ";

            var payload = new { query = $"{query} {{ {gql} }}", variables = new { org = GITHUB_ORG, repo = GITHUB_REPO } };

            const string migrationLogUrl = "MIGRATION_LOG_URL";
            var response = $@"
            {{
                ""data"": {{
                    ""organization"": {{
                        ""repositoryMigrations"": {{
                            ""nodes"": [
                                {{
                                    ""migrationLogUrl"": ""{migrationLogUrl}""
                                }}
                            ]
                        }}
                    }}
                }}
            }}";

            _githubClientMock
                .Setup(m => m.PostAsync(url, It.Is<object>(x => x.ToJson() == payload.ToJson()), null))
                .ReturnsAsync(response);

            // Act
            var expectedMigrationLog = await _githubApi.GetMigrationLogUrl(GITHUB_ORG, GITHUB_REPO);

            // Assert
            expectedMigrationLog.Should().Be(migrationLogUrl);
        }

        [Fact]
        public async Task GetMigrationLogUrl_Retries_On_GQL_Error()
        {
            // Arrange
            const string url = "https://api.github.com/graphql";

            var query = "query ($org: String!, $repo: String!)";
            var gql = @"
                organization(login: $org) {
                    repositoryMigrations(last: 1, repositoryName: $repo) {
                        nodes {
                            migrationLogUrl
                        }
                    }
                }
            ";

            var payload = new { query = $"{query} {{ {gql} }}", variables = new { org = GITHUB_ORG, repo = GITHUB_REPO } };

            const string migrationLogUrl = "MIGRATION_LOG_URL";
            var response = $@"
            {{
                ""data"": {{
                    ""organization"": {{
                        ""repositoryMigrations"": {{
                            ""nodes"": [
                                {{
                                    ""migrationLogUrl"": ""{migrationLogUrl}""
                                }}
                            ]
                        }}
                    }}
                }}
            }}";

            _githubClientMock
                .SetupSequence(m => m.PostAsync(url, It.Is<object>(x => x.ToJson() == payload.ToJson()), null))
                .ReturnsAsync(GQL_ERROR_RESPONSE)
                .ReturnsAsync(GQL_ERROR_RESPONSE)
                .ReturnsAsync(response);

            // Act
            var expectedMigrationLog = await _githubApi.GetMigrationLogUrl(GITHUB_ORG, GITHUB_REPO);

            // Assert
            expectedMigrationLog.Should().Be(migrationLogUrl);
        }

        [Fact]
        public async Task GetMigrationLogUrl_Returns_Null_When_No_Migration()
        {
            // Arrange
            const string url = "https://api.github.com/graphql";

            var query = "query ($org: String!, $repo: String!)";
            var gql = @"
                organization(login: $org) {
                    repositoryMigrations(last: 1, repositoryName: $repo) {
                        nodes {
                            migrationLogUrl
                        }
                    }
                }
            ";

            var payload = new { query = $"{query} {{ {gql} }}", variables = new { org = GITHUB_ORG, repo = GITHUB_REPO } };
            var response = @"
            {
                ""data"": {
                    ""organization"": {
                        ""repositoryMigrations"": {
                            ""nodes"": [
                            ]
                        }
                    }
                }
            }";

            _githubClientMock
                .Setup(m => m.PostAsync(url, It.Is<object>(x => x.ToJson() == payload.ToJson()), null))
                .ReturnsAsync(response);

            // Act
            var expectedMigrationLog = await _githubApi.GetMigrationLogUrl(GITHUB_ORG, GITHUB_REPO);

            // Assert
            expectedMigrationLog.Should().Be(null);
        }

        [Fact]
        public async Task GetIdpGroupId_Returns_The_Idp_Group_Id()
        {
            // Arrange
            const string groupName = "GROUP_NAME";

            var url = $"https://api.github.com/orgs/{GITHUB_ORG}/external-groups";
            const int expectedGroupId = 123;
            var response = $@"
            {{
                ""groups"": [
                    {{
                       ""group_id"": ""{expectedGroupId}"",
                       ""group_name"": ""{groupName}"",
                       ""updated_at"": ""2021-01-24T11:31:04-06:00""
                    }},
                    {{
                       ""group_id"": ""456"",
                       ""group_name"": ""Octocat admins"",
                       ""updated_at"": ""2021-03-24T11:31:04-06:00""
                    }},
                ]
            }}";

            _githubClientMock
                .Setup(m => m.GetAsync(url, null))
                .ReturnsAsync(response);

            // Act
            var actualGroupId = await _githubApi.GetIdpGroupId(GITHUB_ORG, groupName);

            // Assert
            actualGroupId.Should().Be(expectedGroupId);
        }

        [Fact]
        public async Task GetTeamSlug_Returns_The_Team_Slug()
        {
            // Arrange
            const string teamName = "TEAM_NAME";

            var url = $"https://api.github.com/orgs/{GITHUB_ORG}/teams";
            const string expectedTeamSlug = "justice-league";

            var response = new[]
            {
                new
                {
                    id = 1,
                    node_id = "MDQ6VGVhbTE=",
                    url = "https://api.github.com/teams/1",
                    html_url = "https://github.com/orgs/github/teams/justice-league",
                    name = teamName,
                    slug = expectedTeamSlug,
                    description = "A great team.",
                    privacy = "closed",
                    permission = "admin",
                    members_url = "https://api.github.com/teams/1/members/member",
                    repositories_url = "https://api.github.com/teams/1/repos",
                }
            }.ToAsyncJTokenEnumerable();

            _githubClientMock
                .Setup(m => m.GetAllAsync(url, null))
                .Returns(response);

            // Act
            var actualTeamSlug = await _githubApi.GetTeamSlug(GITHUB_ORG, teamName);

            // Assert
            actualTeamSlug.Should().Be(expectedTeamSlug);
        }

        [Fact]
        public async Task AddEmuGroupToTeam_Calls_The_Right_Endpoint_With_Payload()
        {
            // Arrange
            const string teamSlug = "TEAM_SLUG";
            const int groupId = 1;

            var url = $"https://api.github.com/orgs/{GITHUB_ORG}/teams/{teamSlug}/external-groups";
            var payload = new { group_id = groupId };

            // Act
            await _githubApi.AddEmuGroupToTeam(GITHUB_ORG, teamSlug, groupId);

            // Assert
            _githubClientMock.Verify(m => m.PatchAsync(url, It.Is<object>(x => x.ToJson() == payload.ToJson()), null));
        }

        [Fact]
        public async Task AddEmuGroupToTeam_Retries_On_400()
        {
            // Arrange
            const string teamSlug = "TEAM_SLUG";
            const int groupId = 1;

            var url = $"https://api.github.com/orgs/{GITHUB_ORG}/teams/{teamSlug}/external-groups";
            var payload = new { group_id = groupId };

            _githubClientMock.SetupSequence(m => m.PatchAsync(url, It.Is<object>(x => x.ToJson() == payload.ToJson()), null))
                .ThrowsAsync(new HttpRequestException(null, null, HttpStatusCode.BadRequest))
                .ReturnsAsync("");

            // Act
            await _githubApi.AddEmuGroupToTeam(GITHUB_ORG, teamSlug, groupId);

            // Assert
            _githubClientMock.Verify(m => m.PatchAsync(url, It.Is<object>(x => x.ToJson() == payload.ToJson()), null), Times.Exactly(2));
        }

        [Fact]
        public async Task GrantMigratorRole_Returns_True_On_Success()
        {
            // Arrange
            const string actor = "ACTOR";
            const string actorType = "ACTOR_TYPE";
            const string url = "https://api.github.com/graphql";

            var payload =
                "{\"query\":\"mutation grantMigratorRole ( $organizationId: ID!, $actor: String!, $actor_type: ActorType! ) " +
                "{ grantMigratorRole( input: {organizationId: $organizationId, actor: $actor, actorType: $actor_type }) { success } }\"" +
                $",\"variables\":{{\"organizationId\":\"{GITHUB_ORG}\",\"actor\":\"{actor}\",\"actor_type\":\"{actorType}\"}}," +
                "\"operationName\":\"grantMigratorRole\"}";
            const bool expectedSuccessState = true;
            var response = $@"
            {{
                ""data"": {{
                    ""grantMigratorRole"": {{
                        ""success"": {expectedSuccessState.ToString().ToLower()}
                    }}
                }}
            }}";

            _githubClientMock
                .Setup(m => m.PostAsync(url, It.Is<object>(x => x.ToJson() == payload), null))
                .ReturnsAsync(response);

            // Act
            var actualSuccessState = await _githubApi.GrantMigratorRole(GITHUB_ORG, actor, actorType);

            // Assert
            actualSuccessState.Should().BeTrue();
        }

        [Fact]
        public async Task GrantMigratorRole_Returns_False_On_HttpRequestException()
        {
            // Arrange
            const string actor = "ACTOR";
            const string actorType = "ACTOR_TYPE";
            const string url = "https://api.github.com/graphql";

            var payload =
                "{\"query\":\"mutation grantMigratorRole ( $organizationId: ID!, $actor: String!, $actor_type: ActorType! ) " +
                "{ grantMigratorRole( input: {organizationId: $organizationId, actor: $actor, actorType: $actor_type }) { success } }\"" +
                $",\"variables\":{{\"organizationId\":\"{GITHUB_ORG}\",\"actor\":\"{actor}\",\"actor_type\":\"{actorType}\"}}," +
                "\"operationName\":\"grantMigratorRole\"}";

            _githubClientMock
                .Setup(m => m.PostAsync(url, It.Is<object>(x => x.ToJson() == payload), null))
                .Throws<HttpRequestException>();

            // Act
            var actualSuccessState = await _githubApi.GrantMigratorRole(GITHUB_ORG, actor, actorType);

            // Assert
            actualSuccessState.Should().BeFalse();
        }

        [Fact]
        public async Task RevokeMigratorRole_Returns_True_On_Success()
        {
            // Arrange
            const string actor = "ACTOR";
            const string actorType = "ACTOR_TYPE";
            const string url = "https://api.github.com/graphql";

            var payload =
                "{\"query\":\"mutation revokeMigratorRole ( $organizationId: ID!, $actor: String!, $actor_type: ActorType! ) " +
                "{ revokeMigratorRole( input: {organizationId: $organizationId, actor: $actor, actorType: $actor_type }) { success } }\"" +
                $",\"variables\":{{\"organizationId\":\"{GITHUB_ORG}\",\"actor\":\"{actor}\",\"actor_type\":\"{actorType}\"}}," +
                "\"operationName\":\"revokeMigratorRole\"}";
            const bool expectedSuccessState = true;
            var response = $@"
            {{
                ""data"": {{
                    ""revokeMigratorRole"": {{
                        ""success"": {expectedSuccessState.ToString().ToLower()}
                    }}
                }}
            }}";

            _githubClientMock
                .Setup(m => m.PostAsync(url, It.Is<object>(x => x.ToJson() == payload), null))
                .ReturnsAsync(response);

            // Act
            var actualSuccessState = await _githubApi.RevokeMigratorRole(GITHUB_ORG, actor, actorType);

            // Assert
            actualSuccessState.Should().BeTrue();
        }

        [Fact]
        public async Task RevokeMigratorRole_Returns_False_On_HttpRequestException()
        {
            // Arrange
            const string actor = "ACTOR";
            const string actorType = "ACTOR_TYPE";
            const string url = "https://api.github.com/graphql";

            var payload =
                "{\"query\":\"mutation revokeMigratorRole ( $organizationId: ID!, $actor: String!, $actor_type: ActorType! ) " +
                "{ revokeMigratorRole( input: {organizationId: $organizationId, actor: $actor, actorType: $actor_type }) { success } }\"" +
                $",\"variables\":{{\"organizationId\":\"{GITHUB_ORG}\",\"actor\":\"{actor}\",\"actor_type\":\"{actorType}\"}}," +
                "\"operationName\":\"revokeMigratorRole\"}";

            _githubClientMock
                .Setup(m => m.PostAsync(url, It.Is<object>(x => x.ToJson() == payload), null))
                .Throws<HttpRequestException>();

            // Act
            var actualSuccessState = await _githubApi.RevokeMigratorRole(GITHUB_ORG, actor, actorType);

            // Assert
            actualSuccessState.Should().BeFalse();
        }

        [Fact]
        public async Task DeleteRepo_Calls_The_Right_Endpoint()
        {
            // Arrange
            var url = $"https://api.github.com/repos/{GITHUB_ORG}/{GITHUB_REPO}";

            // Act
            await _githubApi.DeleteRepo(GITHUB_ORG, GITHUB_REPO);

            // Assert
            _githubClientMock.Verify(m => m.DeleteAsync(url, null));
        }

        [Fact]
        public async Task GetUserId_Returns_The_User_Id()
        {
            // Arrange
            const string login = "mona";
            const string userId = "NDQ5VXNlcjc4NDc5MzU=";

            var url = $"https://api.github.com/graphql";
            var payload =
                $"{{\"query\":\"query($login: String!) {{user(login: $login) {{ id, name }} }}\",\"variables\":{{\"login\":\"{login}\"}}}}";

            var response = $@"
            {{
                ""data"": 
                    {{
                        ""user"": 
                            {{
                                ""id"": ""{userId}"",
                                ""name"": ""{login}"" 
                            }} 
                    }} 
            }}";

            _githubClientMock
                .Setup(m => m.PostAsync(url, It.Is<object>(x => x.ToJson() == payload), null))
                .ReturnsAsync(response);

            // Act
            var result = await _githubApi.GetUserId(login);

            // Assert
            result.Should().Be(userId);
        }

        [Fact]
        public async Task GetUserId_For_No_Existant_User_Returns_Null()
        {
            // Arrange
            const string login = "idonotexist";

            var url = $"https://api.github.com/graphql";
            var payload =
                $"{{\"query\":\"query($login: String!) {{user(login: $login) {{ id, name }} }}\",\"variables\":{{\"login\":\"{login}\"}}}}";

            var response = @"{
	            ""data"": {
                    ""user"": null
                },
	            ""errors"": [
		            {
			            ""type"": ""NOT_FOUND"",
			            ""path"": [
				            ""user""
			            ],
			            ""locations"": [
				            {
					            ""line"": 4,
					            ""column"": 3
                            }
			            ],
			            ""message"": ""Could not resolve to a User with the login of 'idonotexist'.""
		            }
	            ]
            }";

            _githubClientMock
                .Setup(m => m.PostAsync(url, It.Is<object>(x => x.ToJson() == payload), null))
                .ReturnsAsync(response);

            // Act
            var result = await _githubApi.GetUserId(login);

            // Assert
            result.Should().BeNull();
        }

        [Fact]
        public async Task ReclaimMannequin_Returns_Error()
        {
            // Arrange
            const string orgId = "dummyorgid";
            const string mannequinId = "NDQ5VXNlcjc4NDc5MzU=";
            const string targetUserId = "ND5TVXNlcjc4NDc5MzU=";

            var url = $"https://api.github.com/graphql";

            var payload = @"{""query"":""mutation($orgId: ID!,$sourceId: ID!,$targetId: ID!) { createAttributionInvitation(
		            input: { ownerId: $orgId, sourceId: $sourceId, targetId: $targetId }
	            ) {
		            source {
			            ... on Mannequin {
				            id
				            login
			            }
		            }

		            target {
			            ... on User {
				            id
				            login
			            }
		            }
                }
            }""" + $",\"variables\":{{\"orgId\":\"{orgId}\", \"sourceId\":\"{mannequinId}\", \"targetId\":\"{targetUserId}\"}}}}";

            var response = $@"{{
                ""data"": {{
                                ""createAttributionInvitation"": null
                    }},
                ""errors"": [{{
                                ""type"": ""UNPROCESSABLE"",
                    ""path"": [""createAttributionInvitation""],
                    ""locations"": [{{
                                        ""line"": 2,
                        ""column"": 14
                    }}],
                    ""message"": ""Target must be a member of the octocat organization""
                }}]
            }}";

            var expectedReclaimMannequinResponse = new MannequinReclaimResult()
            {
                Data = new CreateAttributionInvitationData()
                {
                    CreateAttributionInvitation = null
                },
                Errors = new Collection<ErrorData>{new ErrorData
                {
                    Type = "UNPROCESSABLE",
                    Message = "Target must be a member of the octocat organization",
                    Path = new Collection<string> { "createAttributionInvitation" },
                    Locations = new Collection<Location> {
                                new Location()
                                {
                                    Line = 2,
                                    Column = 14
                                }
                            }
                    }
                }
            };

            _githubClientMock
                .Setup(m => m.PostAsync(url,
                It.Is<object>(x => Compact(x.ToJson()) == Compact(payload)), null))
                    .ReturnsAsync(response);

            // Act
            var result = await _githubApi.ReclaimMannequin(orgId, mannequinId, targetUserId);

            // Assert
            result.Should().BeEquivalentTo(expectedReclaimMannequinResponse);
        }

        [Fact]
        public async Task GetMannequins_Returns_NoMannequins()
        {
            // Arrange
            const string orgId = "ORG_ID";

            var url = $"https://api.github.com/graphql";

            var payload =
    @"{""query"":""query($id: ID!, $first: Int, $after: String) { 
                node(id: $id) {
                    ... on Organization {
                        mannequins(first: $first, after: $after) {
                            pageInfo {
                                endCursor
                                hasNextPage
                            }
                            nodes {
                                login
                                id
                                claimant {
                                    login
                                    id
                                }
                            }
                        }
                    }
                }
            }""" +
    $",\"variables\":{{\"id\":\"{orgId}\"}}}}";

            _githubClientMock
                .Setup(m => m.PostGraphQLWithPaginationAsync(
                    url,
                    It.Is<object>(x => Compact(x.ToJson()) == Compact(payload)),
                    It.IsAny<Func<JObject, JArray>>(),
                    It.IsAny<Func<JObject, JObject>>(),
                    It.IsAny<int>(),
                    null,
                    null))
                    .Returns(Array.Empty<JToken>().ToAsyncEnumerable());

            // Act
            var result = await _githubApi.GetMannequins(orgId);

            // Assert
            result.Count().Should().Be(0);
        }

        [Fact]
        public async Task GetMannequins_Returns_Mannequins()
        {
            // Arrange
            const string orgId = "ORG_ID";
            const string login1 = "mona";
            const string login2 = "monalisa";

            var url = $"https://api.github.com/graphql";

            var payload =
    @"{""query"":""query($id: ID!, $first: Int, $after: String) { 
                node(id: $id) {
                    ... on Organization {
                        mannequins(first: $first, after: $after) {
                            pageInfo {
                                endCursor
                                hasNextPage
                            }
                            nodes {
                                login
                                id
                                claimant {
                                    login
                                    id
                                }
                            }
                        }
                    }
                }
            }""" +
    $",\"variables\":{{\"id\":\"{orgId}\"}}}}";

            var mannequin1 = new
            {
                login = "mona",
                id = "DUMMYID1",
                claimant = new { }
            };
            var mannequin2 = new
            {
                login = "monalisa",
                id = "DUMMYID2",
                claimant = new
                {
                    login = "monareclaimed",
                    id = "TARGETDUMMYID"
                }
            };

            _githubClientMock
                .Setup(m => m.PostGraphQLWithPaginationAsync(
                    url,
                    It.Is<object>(x => Compact(x.ToJson()) == Compact(payload)),
                    It.IsAny<Func<JObject, JArray>>(),
                    It.IsAny<Func<JObject, JObject>>(),
                    It.IsAny<int>(),
                    null,
                    null))
                    .Returns(new[]
                        {
                            JToken.FromObject(mannequin1),
                            JToken.FromObject(mannequin2),
                        }.ToAsyncEnumerable());

            // Act
            var result = await _githubApi.GetMannequins(orgId);

            // Assert
            result.Count().Should().Be(2);

            var mannequinsResult = result.ToArray();
            mannequinsResult[0].Id.Should().Be("DUMMYID1");
            mannequinsResult[0].Login.Should().Be(login1);
            mannequinsResult[0].MappedUser.Should().BeNull();

            mannequinsResult[1].Id.Should().Be("DUMMYID2");
            mannequinsResult[1].Login.Should().Be(login2);
            mannequinsResult[1].MappedUser.Should().NotBeNull();
            mannequinsResult[1].MappedUser.Login.Should().Be("monareclaimed");
            mannequinsResult[1].MappedUser.Id.Should().Be("TARGETDUMMYID");
        }

        [Fact]
        public async Task GetMannequins_Retries_On_Error()
        {
            // Arrange
            const string orgId = "ORG_ID";
            const string login1 = "mona";
            const string login2 = "monalisa";

            var url = $"https://api.github.com/graphql";

            var payload =
    @"{""query"":""query($id: ID!, $first: Int, $after: String) { 
                node(id: $id) {
                    ... on Organization {
                        mannequins(first: $first, after: $after) {
                            pageInfo {
                                endCursor
                                hasNextPage
                            }
                            nodes {
                                login
                                id
                                claimant {
                                    login
                                    id
                                }
                            }
                        }
                    }
                }
            }""" +
    $",\"variables\":{{\"id\":\"{orgId}\"}}}}";

            var mannequin1 = new
            {
                login = "mona",
                id = "DUMMYID1",
                claimant = new { }
            };
            var mannequin2 = new
            {
                login = "monalisa",
                id = "DUMMYID2",
                claimant = new
                {
                    login = "monareclaimed",
                    id = "TARGETDUMMYID"
                }
            };

            _githubClientMock
                .SetupSequence(m => m.PostGraphQLWithPaginationAsync(
                    url,
                    It.Is<object>(x => Compact(x.ToJson()) == Compact(payload)),
                    It.IsAny<Func<JObject, JArray>>(),
                    It.IsAny<Func<JObject, JObject>>(),
                    It.IsAny<int>(),
                    null,
                    null))
                    .Throws(new InvalidOperationException())
                    .Throws(new InvalidOperationException())
                    .Returns(new[]
                        {
                            JToken.FromObject(mannequin1),
                            JToken.FromObject(mannequin2),
                        }.ToAsyncEnumerable());

            // Act
            var result = await _githubApi.GetMannequins(orgId);

            // Assert
            result.Count().Should().Be(2);

            var mannequinsResult = result.ToArray();
            mannequinsResult[0].Id.Should().Be("DUMMYID1");
            mannequinsResult[0].Login.Should().Be(login1);
            mannequinsResult[0].MappedUser.Should().BeNull();

            mannequinsResult[1].Id.Should().Be("DUMMYID2");
            mannequinsResult[1].Login.Should().Be(login2);
            mannequinsResult[1].MappedUser.Should().NotBeNull();
            mannequinsResult[1].MappedUser.Login.Should().Be("monareclaimed");
            mannequinsResult[1].MappedUser.Id.Should().Be("TARGETDUMMYID");
        }

        [Fact]
        public async Task ReclaimMannequin_Returns_Success()
        {
            // Arrange
            const string orgId = "dummyorgid";
            const string mannequinId = "NDQ5VXNlcjc4NDc5MzU=";
            const string mannequinUser = "mona";
            const string targetUserId = "ND5TVXNlcjc4NDc5MzU=";
            const string targetUser = "lisa";

            var url = $"https://api.github.com/graphql";

            var payload = @"{""query"":""mutation($orgId: ID!,$sourceId: ID!,$targetId: ID!) { createAttributionInvitation(
		            input: { ownerId: $orgId, sourceId: $sourceId, targetId: $targetId }
	            ) {
		            source {
			            ... on Mannequin {
				            id
				            login
			            }
		            }

		            target {
			            ... on User {
				            id
				            login
			            }
		            }
                }
            }""" + $",\"variables\":{{\"orgId\":\"{orgId}\", \"sourceId\":\"{mannequinId}\", \"targetId\":\"{targetUserId}\"}}}}";

            var response = $@"{{
                ""data"": {{
                    ""createAttributionInvitation"": {{
                        ""source"": {{
                            ""id"": ""{mannequinId}"",
                            ""login"": ""{mannequinUser}""
                        }},
                        ""target"": {{
                            ""id"": ""{targetUserId}"",
                            ""login"": ""{targetUser}""
                        }}
                    }}
                }}
            }}";

            var expectedReclaimMannequinResponse = new MannequinReclaimResult()
            {
                Data = new CreateAttributionInvitationData()
                {
                    CreateAttributionInvitation = new CreateAttributionInvitation()
                    {
                        Source = new UserInfo()
                        {
                            Id = mannequinId,
                            Login = mannequinUser
                        },
                        Target = new UserInfo()
                        {
                            Id = targetUserId,
                            Login = targetUser
                        }
                    }
                }
            };

            _githubClientMock
                .Setup(m => m.PostAsync(url,
                It.Is<object>(x => Compact(x.ToJson()) == Compact(payload)), null))
                    .ReturnsAsync(response);

            // Act
            var result = await _githubApi.ReclaimMannequin(orgId, mannequinId, targetUserId);

            // Assert
            result.Should().BeEquivalentTo(expectedReclaimMannequinResponse);
        }

        [Fact]
        public async Task StartMetadataArchiveGeneration_Returns_The_Initiated_Migration_Id()
        {
            // Arrange
            const string url = $"https://api.github.com/orgs/{GITHUB_ORG}/migrations";
            var payload = new
            {
                repositories = new[] { GITHUB_REPO },
                exclude_git_data = true,
                exclude_releases = false,
                lock_repositories = false,
                exclude_owner_projects = true
            };
            const int expectedMigrationId = 1;
            var response = new { id = expectedMigrationId };

            _githubClientMock
                .Setup(m => m.PostAsync(url, It.Is<object>(x => x.ToJson() == payload.ToJson()), null))
                .ReturnsAsync(response.ToJson());

            // Act
            var actualMigrationId = await _githubApi.StartMetadataArchiveGeneration(GITHUB_ORG, GITHUB_REPO, false, false);

            // Assert
            actualMigrationId.Should().Be(expectedMigrationId);
        }

        [Fact]
        public async Task StartMetadataArchiveGeneration_Excludes_Releases_When_Skip_Releases_Is_True()
        {
            // Arrange
            const string url = $"https://api.github.com/orgs/{GITHUB_ORG}/migrations";
            var payload = new
            {
                repositories = new[] { GITHUB_REPO },
                exclude_git_data = true,
                exclude_releases = true,
                lock_repositories = false,
                exclude_owner_projects = true
            };
            var response = new { id = 1 };

            _githubClientMock.Setup(m => m.PostAsync(url, It.IsAny<object>(), null)).ReturnsAsync(response.ToJson());

            // Act
            await _githubApi.StartMetadataArchiveGeneration(GITHUB_ORG, GITHUB_REPO, true, false);

            // Assert
            _githubClientMock.Verify(m => m.PostAsync(url, It.Is<object>(x => x.ToJson() == payload.ToJson()), null));
        }

        [Fact]
        public async Task StartMetadataArchiveGeneration_Locks_Repos_When_Lock_Source_Repo_Is_True()
        {
            // Arrange
            const string url = $"https://api.github.com/orgs/{GITHUB_ORG}/migrations";
            var payload = new
            {
                repositories = new[] { GITHUB_REPO },
                exclude_git_data = true,
                exclude_releases = true,
                lock_repositories = true,
                exclude_owner_projects = true
            };
            var response = new { id = 1 };

            _githubClientMock.Setup(m => m.PostAsync(url, It.IsAny<object>(), null)).ReturnsAsync(response.ToJson());

            // Act
            await _githubApi.StartMetadataArchiveGeneration(GITHUB_ORG, GITHUB_REPO, true, true);

            // Assert
            _githubClientMock.Verify(m => m.PostAsync(url, It.Is<object>(x => x.ToJson() == payload.ToJson()), null));
        }

        [Fact]
        public async Task StartGitArchiveGeneration_Returns_The_Initiated_Migration_Id()
        {
            // Arrange
            const string url = $"https://api.github.com/orgs/{GITHUB_ORG}/migrations";
            var payload = new
            {
                repositories = new[] { GITHUB_REPO },
                exclude_metadata = true
            };
            const int expectedMigrationId = 1;
            var response = new { id = expectedMigrationId };

            _githubClientMock
                .Setup(m => m.PostAsync(url, It.Is<object>(x => x.ToJson() == payload.ToJson()), null))
                .ReturnsAsync(response.ToJson());

            // Act
            var actualMigrationId = await _githubApi.StartGitArchiveGeneration(GITHUB_ORG, GITHUB_REPO);

            // Assert
            actualMigrationId.Should().Be(expectedMigrationId);
        }

        [Fact]
        public async Task StartGitArchiveGeneration_Throws_Octoshift_CLI_Exception_When_Blob_Storage_Settings_Are_Not_Set()
        {
            // Arrange
            const string url = $"https://api.github.com/orgs/{GITHUB_ORG}/migrations";
            var payload = new
            {
                repositories = new[] { GITHUB_REPO },
                exclude_metadata = true
            };
            var exception_message = "Before you can start a migration, you must configure blob storage settings in your management console.";

            _githubClientMock
                .Setup(m => m.PostAsync(url, It.Is<object>(x => x.ToJson() == payload.ToJson()), null))
                    .ThrowsAsync(new HttpRequestException(exception_message, null, HttpStatusCode.BadGateway));

            // Act
            await _githubApi.Invoking(api => api.StartGitArchiveGeneration(GITHUB_ORG, GITHUB_REPO))
                .Should()
                .ThrowExactlyAsync<OctoshiftCliException>()
                .WithMessage(exception_message);
        }


        [Fact]
        public async Task GetSecretScanningAlertsData()
        {
            // Arrange
            const string url =
                $"https://api.github.com/repos/{GITHUB_ORG}/{GITHUB_REPO}/secret-scanning/alerts?per_page=100";

            var secretScanningAlert_1 = $@"
                {{
                    ""number"": 18,
                    ""created_at"": ""2022-05-05T21:40:00Z"",
                    ""updated_at"": ""2022-05-05T21:40:00Z"",
                    ""url"": ""https://api.github.com/repos/{GITHUB_ORG}/{GITHUB_REPO}/secret-scanning/alerts/18"",
                    ""html_url"": ""https://github.com/{GITHUB_ORG}/{GITHUB_REPO}/security/secret-scanning/18"",
                    ""locations_url"": ""https://api.github.com/repos/{GITHUB_ORG}/{GITHUB_REPO}/secret-scanning/alerts/18/locations"",
                    ""state"": ""open"",
                    ""secret_type"": ""mcantu_pattern"",
                    ""secret_type_display_name"": ""mcantu pattern"",
                    ""secret"": ""my_secret_pattern_129"",
                    ""resolution"": null,
                    ""resolved_by"": null,
                    ""resolved_at"": null,
                    ""push_protection_bypassed"": false,
                    ""push_protection_bypassed_by"": null,
                    ""push_protection_bypassed_at"": null
                }}
            ";

            var secretScanningAlert_2 = $@"
                {{
                    ""number"": 15,
                    ""created_at"": ""2021-12-21T16:41:07Z"",
                    ""updated_at"": ""2022-04-05T20:57:03Z"",
                    ""url"": ""https://api.github.com/repos/{GITHUB_ORG}/{GITHUB_REPO}/secret-scanning/alerts/15"",
                    ""html_url"": ""https://github.com/{GITHUB_ORG}/{GITHUB_REPO}/security/secret-scanning/15"",
                    ""locations_url"": ""https://api.github.com/repos/{GITHUB_ORG}/{GITHUB_REPO}/secret-scanning/alerts/15/locations"",
                    ""state"": ""resolved"",
                    ""secret_type"": ""mcantu_pattern"",
                    ""secret_type_display_name"": ""mcantu pattern"",
                    ""secret"": ""my_secret_pattern_124"",
                    ""resolution"": null,
                    ""resolved_by"": {{
                        ""login"": ""leftrightleft"",
                        ""id"": 4910518,
                        ""node_id"": ""MDQ6VXNlcjQ5MTA1MTg="",
                        ""avatar_url"": ""https://avatars.githubusercontent.com/u/4910518?v=4"",
                        ""gravatar_id"": """",
                        ""url"": ""https://api.github.com/users/leftrightleft"",
                        ""html_url"": ""https://github.com/leftrightleft"",
                        ""followers_url"": ""https://api.github.com/users/leftrightleft/followers"",
                        ""received_events_url"": ""https://api.github.com/users/leftrightleft/received_events"",
                        ""type"": ""User"",
                        ""site_admin"": true
                    }},
                    ""resolved_at"": ""2022-04-05T20:57:03"",
                    ""push_protection_bypassed"": false,
                    ""push_protection_bypassed_by"": null,
                    ""push_protection_bypassed_at"": null
                }}
            ";

            var secretScanningAlert_3 = $@"
                {{
                    ""number"": 1,
		            ""created_at"": ""2020-04-17T03:30:33Z"",
                    ""updated_at"": ""2021-10-01T15:27:33Z"",
                    ""url"": ""https://api.github.com/repos/octodemo/demo-vulnerabilities-ghas/secret-scanning/alerts/1"",
                    ""html_url"": ""https://github.com/octodemo/demo-vulnerabilities-ghas/security/secret-scanning/1"",
                    ""locations_url"": ""https://api.github.com/repos/octodemo/demo-vulnerabilities-ghas/secret-scanning/alerts/1/locations"",
                    ""state"": ""open"",
                    ""secret_type"": ""github_personal_access_token"",
                    ""secret_type_display_name"": ""GitHub Personal Access Token"",
                    ""secret"": ""fb5xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx550b82"",
                    ""resolution"": null,
		            ""resolved_by"": null,
		            ""resolved_at"": null,
		            ""push_protection_bypassed"": false,
		            ""push_protection_bypassed_by"": null,
		            ""push_protection_bypassed_at"": null
	            }}
            ";

            var secretScanningAlert_4 = $@"
                {{
                    ""number"": 1,
		            ""created_at"": ""2022-08-10T07:58:30Z"",
                    ""updated_at"": ""2022-08-15T13:53:42Z"",
                    ""url"": ""https://api.github.com/repos/{GITHUB_ORG}/{GITHUB_REPO}/secret-scanning/alerts/1"",
                    ""html_url"": ""https://github.com/{GITHUB_ORG}/{GITHUB_REPO}/security/secret-scanning/1"",
                    ""locations_url"": ""https://api.github.com/repos/{GITHUB_ORG}/{GITHUB_REPO}/secret-scanning/alerts/1/locations"",
                    ""state"": ""resolved"",
                    ""secret_type"": ""google_api_key"",
                    ""secret_type_display_name"": ""Google API Key"",
                    ""secret"": ""AIzaSyAxxxxxxxxxxxxxxxxxxxxxxxxxxxxt2Q"",
                    ""resolution"": ""false_positive"",
                    ""resolved_by"": {{
                        ""login"": ""peter-murray"",
                        ""id"": 681306,
                        ""node_id"": ""MDQ6VXNlcjY4MTMwNg=="",
                        ""avatar_url"": ""https://avatars.githubusercontent.com/u/681306?v=4"",
                        ""gravatar_id"": """",
                        ""url"": ""https://api.github.com/users/peter-murray"",
                        ""html_url"": ""https://github.com/peter-murray"",
                        ""followers_url"": ""https://api.github.com/users/peter-murray/followers"",
                        ""following_url"": ""https://api.github.com/users/peter-murray/following{{/other_user}}"",
                        ""gists_url"": ""https://api.github.com/users/peter-murray/gists{{/gist_id}}"",
                        ""starred_url"": ""https://api.github.com/users/peter-murray/starred{{/owner}}{{/repo}}"",
                        ""subscriptions_url"": ""https://api.github.com/users/peter-murray/subscriptions"",
                        ""organizations_url"": ""https://api.github.com/users/peter-murray/orgs"",
                        ""repos_url"": ""https://api.github.com/users/peter-murray/repos"",
                        ""events_url"": ""https://api.github.com/users/peter-murray/events{{/privacy}}"",
                        ""received_events_url"": ""https://api.github.com/users/peter-murray/received_events"",
                        ""type"": ""User"",
                        ""site_admin"": true
                    }},
                    ""resolved_at"": ""2022-08-15T13:53:42Z"",
                    ""push_protection_bypassed"": false,
		            ""push_protection_bypassed_by"": null,
		            ""push_protection_bypassed_at"": null
	            }}
            ";

            var responsePage1 = $@"
                [
                    {secretScanningAlert_1},
                    {secretScanningAlert_2},
                ]
            ";

            var responsePage2 = $@"
                [
                    {secretScanningAlert_3},
                    {secretScanningAlert_4},
                ]
            ";

            async IAsyncEnumerable<JToken> GetAllPages()
            {
                var jArrayPage1 = JArray.Parse(responsePage1);
                yield return jArrayPage1[0];
                yield return jArrayPage1[1];

                var jArrayPage2 = JArray.Parse(responsePage2);
                yield return jArrayPage2[0];
                yield return jArrayPage2[1];

                await Task.CompletedTask;
            }

            _githubClientMock
                .Setup(m => m.GetAllAsync(url, null))
                .Returns(GetAllPages);

            // Act
            var scanResults = await _githubApi.GetSecretScanningAlertsForRepository(GITHUB_ORG, GITHUB_REPO);

            // Assert
            scanResults.Count().Should().Be(4);
            var scanResultsArray = scanResults.ToArray();
            AssertSecretScanningData(scanResultsArray[0], JObject.Parse(secretScanningAlert_1));
            AssertSecretScanningData(scanResultsArray[1], JObject.Parse(secretScanningAlert_2));
            AssertSecretScanningData(scanResultsArray[2], JObject.Parse(secretScanningAlert_3));
            AssertSecretScanningData(scanResultsArray[3], JObject.Parse(secretScanningAlert_4));
        }

        [Fact]
        public async Task GetSecretScanningAlertLocationData()
        {
            // Arrange
            const int alert = 1;
            var url = $"https://api.github.com/repos/{GITHUB_ORG}/{GITHUB_REPO}/secret-scanning/alerts/{alert}/locations?per_page=100";

            var alertLocation_1 = $@"
                {{
                    ""type"": ""commit"",
                    ""details"": {{
                        ""path"": ""src/test/java/com/github/demo/service/BookServiceTest.java"",
                        ""start_line"": 18,
                        ""end_line"": 18,
                        ""start_column"": 46,
                        ""end_column"": 85,
                        ""blob_sha"": ""c9aaf762f939a1e5378d4c5b9584d64672f1069f"",
                        ""blob_url"": ""https://api.github.com/repos/octodemo/pm-ghas-demo/git/blobs/c9aaf762f939a1e5378d4c5b9584d64672f1069f"",
                        ""commit_sha"": ""5b4678d6a8986edf1fbc878ab90bd5efc35ef9db"",
                        ""commit_url"": ""https://api.github.com/repos/octodemo/pm-ghas-demo/git/commits/5b4678d6a8986edf1fbc878ab90bd5efc35ef9db""
                    }}
                }}
            ";

            var alertLocation_2 = $@"
                {{
                    ""type"": ""commit"",
                    ""details"": {{
                        ""path"": ""src/index.js"",
                        ""start_line"": 5,
                        ""end_line"": 5,
                        ""start_column"": 12,
                        ""end_column"": 52,
                        ""blob_sha"": ""2044bb6ccd535142b974776db108c32a19f89e80"",
                        ""blob_url"": ""https://api.github.com/repos/octodemo/demo-vulnerabilities-ghas/git/blobs/2044bb6ccd535142b974776db108c32a19f89e80"",
                        ""commit_sha"": ""c8d00bc80bad56d21bcec32a2a7b74c115dd7bc7"",
                        ""commit_url"": ""https://api.github.com/repos/octodemo/demo-vulnerabilities-ghas/git/commits/c8d00bc80bad56d21bcec32a2a7b74c115dd7bc7""
                    }}
                }}
            ";

            var responsePage1 = $@"
                [
                    {alertLocation_1},
                ]
            ";

            var responsePage2 = $@"
                [
                    {alertLocation_2},
                ]
            ";

            async IAsyncEnumerable<JToken> GetAllPages()
            {
                var jArrayPage1 = JArray.Parse(responsePage1);
                yield return jArrayPage1[0];

                var jArrayPage2 = JArray.Parse(responsePage2);
                yield return jArrayPage2[0];

                await Task.CompletedTask;
            }

            _githubClientMock
                .Setup(m => m.GetAllAsync(url, null))
                .Returns(GetAllPages);

            // Act
            var locations = await _githubApi.GetSecretScanningAlertsLocations(GITHUB_ORG, GITHUB_REPO, alert);

            // Assert
            locations.Count().Should().Be(2);
            var locationsArray = locations.ToArray();

            var location = locationsArray[0];
            var expectedData = JObject.Parse(alertLocation_1);
            location.Path.Should().Be((string)expectedData["details"]["path"]);

            location = locationsArray[1];
            expectedData = JObject.Parse(alertLocation_2);
            location.Path.Should().Be((string)expectedData["details"]["path"]);
        }

        private void AssertSecretScanningData(GithubSecretScanningAlert actual, JToken expectedData)
        {
            actual.Number.Should().Be((int)expectedData["number"]);
            actual.State.Should().Be((string)expectedData["state"]);
            actual.SecretType.Should().Be((string)expectedData["secret_type"]);
            actual.Resolution.Should().Be((string)expectedData["resolution"]);
            actual.Secret.Should().Be((string)expectedData["secret"]);
        }

        [Fact]
        public async Task UpdateSecretScanningAlert_Calls_The_Right_Endpoint_With_Payload_For_Resolved_State()
        {
            // Arrange
            const int alertNumber = 100;
            const string alertState = "resolved";
            const string alertResolution = "wont_fix";

            var url = $"https://api.github.com/repos/{GITHUB_ORG}/{GITHUB_REPO}/secret-scanning/alerts/{alertNumber}";
            var payload = new
            {
                state = alertState,
                resolution = alertResolution
            };

            // Act
            await _githubApi.UpdateSecretScanningAlert(GITHUB_ORG, GITHUB_REPO, alertNumber, alertState, alertResolution);

            // Assert
            _githubClientMock.Verify(m => m.PatchAsync(url, It.Is<object>(x => x.ToJson() == payload.ToJson()), null));
        }

        [Fact]
        public async Task UpdateSecretScanningAlert_Calls_The_Right_Endpoint_With_Payload_For_Open_State()
        {
            // Arrange
            const int alertNumber = 1;
            const string alertState = "open";

            var url = $"https://api.github.com/repos/{GITHUB_ORG}/{GITHUB_REPO}/secret-scanning/alerts/{alertNumber}";
            var payload = new { state = alertState };

            // Act
            await _githubApi.UpdateSecretScanningAlert(GITHUB_ORG, GITHUB_REPO, alertNumber, alertState);

            // Assert
            _githubClientMock.Verify(m => m.PatchAsync(url, It.Is<object>(x => x.ToJson() == payload.ToJson()), null));
        }

        [Fact]
        public async Task GetEnterpriseServerVersion_Returns_Null_If_Not_Enterprise_Server()
        {
            // Arrange
            var url = "https://api.github.com/meta";
            var response = $@"
            {{
                ""verifiable_password_authentication"": true,
                ""packages"": [
                ],
                ""dependabot"": [
                ]
            }}";


            _githubClientMock
                .Setup(m => m.GetAsync(url, null))
                .ReturnsAsync(response);

            // Act
            var version = await _githubApi.GetEnterpriseServerVersion();

            // Assert
            version.Should().BeNull();
        }

        [Fact]
        public async Task GetEnterpriseServerVersion_Returns_Version()
        {
            // Arrange
            var url = "https://api.github.com/meta";
            var response = $@"
            {{
                ""verifiable_password_authentication"": true,
                ""packages"": [
                ],
                ""dependabot"": [
                ],
                ""installed_version"": ""3.7.0""
            }}";

            _githubClientMock
                .Setup(m => m.GetAsync(url, null))
                .ReturnsAsync(response);

            // Act
            var version = await _githubApi.GetEnterpriseServerVersion();

            // Assert
            version.Should().Be("3.7.0");
        }

        private string Compact(string source) =>
            source
                .Replace("\r", "")
                .Replace("\n", "")
                .Replace("\t", "")
                .Replace("\\r", "")
                .Replace("\\n", "")
                .Replace("\\t", "")
                .Replace(" ", "");
    }
}
