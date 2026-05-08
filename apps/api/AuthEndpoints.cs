namespace Kanitel.Api;

using static Kanitel.Api.EndpointHelpers;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/register", async (JsonDataStore store, AuthRegisterRequest request, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.DisplayName) ||
                string.IsNullOrWhiteSpace(request.Email) ||
                string.IsNullOrWhiteSpace(request.Password) ||
                string.IsNullOrWhiteSpace(request.ConfirmPassword))
            {
                return Results.BadRequest(new { error = "Display name, email, password, and password confirmation are required." });
            }

            if (request.Password != request.ConfirmPassword)
            {
                return Results.BadRequest(new { error = "Passwords do not match." });
            }

            if (request.Password.Length < 6)
            {
                return Results.BadRequest(new { error = "Password must be at least 6 characters." });
            }

            var result = await store.MutateAsync<object?>(state =>
            {
                var email = NormalizeEmail(request.Email);
                var existingAccount = state.Accounts.FirstOrDefault(account =>
                    string.Equals(account.Email, email, StringComparison.OrdinalIgnoreCase));
                if (existingAccount is not null)
                {
                    return null;
                }

                var person = state.People.FirstOrDefault(item =>
                    string.Equals(item.Email, email, StringComparison.OrdinalIgnoreCase));
                if (person is null)
                {
                    person = new Person
                    {
                        DisplayName = request.DisplayName.Trim(),
                        Email = email,
                        AvatarUrl = request.AvatarUrl?.Trim() ?? ""
                    };
                    state.People.Add(person);
                }
                else
                {
                    person.DisplayName = request.DisplayName.Trim();
                    person.Email = email;
                    if (request.AvatarUrl is not null)
                    {
                        person.AvatarUrl = request.AvatarUrl.Trim();
                    }
                }

                var salt = NewToken();
                var account = new UserAccount
                {
                    PersonId = person.Id,
                    Email = email,
                    PasswordSalt = salt,
                    PasswordHash = HashPassword(request.Password, salt),
                    SessionToken = NewToken(),
                    LastLoginAt = DateTimeOffset.UtcNow
                };
                state.Accounts.Add(account);

                if (state.Accounts.Count == 1 && state.Projects.Count > 0)
                {
                    var projectId = state.Projects[0].Id;
                    if (state.Members.All(member => member.ProjectId != projectId || member.PersonId != person.Id))
                    {
                        state.Members.Add(new ProjectMember
                        {
                            ProjectId = projectId,
                            PersonId = person.Id
                        });
                    }
                }

                return new AuthResponse(account.SessionToken, person);
            }, cancellationToken);

            return result is null ? Results.Conflict(new { error = "Account already exists." }) : Results.Ok(result);
        })
            .WithTags("Auth")
            .WithSummary("Register a local user")
            .WithDescription("Creates a local account when password and confirmation match, creates or updates the matching person profile, returns an auth token, and grants the first registered account owner access to the seed project.");

        app.MapPost("/api/auth/login", async (JsonDataStore store, AuthLoginRequest request, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.BadRequest(new { error = "Email and password are required." });
            }

            var result = await store.MutateAsync<object?>(state =>
            {
                var email = NormalizeEmail(request.Email);
                var account = state.Accounts.FirstOrDefault(item =>
                    string.Equals(item.Email, email, StringComparison.OrdinalIgnoreCase));
                if (account is null || !VerifyPassword(request.Password, account.PasswordSalt, account.PasswordHash))
                {
                    return null;
                }

                account.SessionToken = NewToken();
                account.LastLoginAt = DateTimeOffset.UtcNow;
                var person = state.People.FirstOrDefault(item => item.Id == account.PersonId);
                return person is null ? null : new AuthResponse(account.SessionToken, person);
            }, cancellationToken);

            return result is null ? Results.Unauthorized() : Results.Ok(result);
        })
            .WithTags("Auth")
            .WithSummary("Login")
            .WithDescription("Validates email and password, rotates the session token, and returns the token with the linked person profile. Use the returned token as `Bearer <token>` in Swagger Authorize.");

        app.MapGet("/api/auth/me", async (JsonDataStore store, HttpRequest httpRequest, CancellationToken cancellationToken) =>
        {
            var state = await store.SnapshotAsync(cancellationToken);
            var currentUser = FindCurrentUser(state, httpRequest);
            return currentUser is null ? Results.Unauthorized() : Results.Ok(currentUser);
        })
            .WithTags("Auth")
            .WithSummary("Read current profile")
            .WithDescription("Reads the current person profile from the bearer token or `X-Kanitel-Token` header. Returns 401 when the token is missing or invalid.");

        app.MapPatch("/api/auth/me", async (JsonDataStore store, HttpRequest httpRequest, ProfileRequest request, CancellationToken cancellationToken) =>
        {
            var token = ReadBearerToken(httpRequest);
            if (string.IsNullOrWhiteSpace(token))
            {
                return Results.Unauthorized();
            }

            var wantsPasswordChange =
                !string.IsNullOrWhiteSpace(request.CurrentPassword) ||
                !string.IsNullOrWhiteSpace(request.NewPassword) ||
                !string.IsNullOrWhiteSpace(request.ConfirmNewPassword);

            if (wantsPasswordChange)
            {
                if (string.IsNullOrWhiteSpace(request.CurrentPassword) ||
                    string.IsNullOrWhiteSpace(request.NewPassword) ||
                    string.IsNullOrWhiteSpace(request.ConfirmNewPassword))
                {
                    return Results.BadRequest(new { error = "Current password, new password, and password confirmation are required." });
                }

                if (request.NewPassword != request.ConfirmNewPassword)
                {
                    return Results.BadRequest(new { error = "Passwords do not match." });
                }

                if (request.NewPassword.Length < 6)
                {
                    return Results.BadRequest(new { error = "Password must be at least 6 characters." });
                }
            }

            var result = await store.MutateAsync<ProfileUpdateResult>(state =>
            {
                var account = state.Accounts.FirstOrDefault(item => item.SessionToken == token);
                if (account is null)
                {
                    return ProfileUpdateResult.Fail("Profile could not be updated.");
                }

                var person = state.People.FirstOrDefault(item => item.Id == account.PersonId);
                if (person is null)
                {
                    return ProfileUpdateResult.Fail("Profile could not be updated.");
                }

                var nextEmail = string.IsNullOrWhiteSpace(request.Email) ? null : NormalizeEmail(request.Email);
                if (!string.IsNullOrWhiteSpace(request.Email))
                {
                    var occupied = state.Accounts.Any(item =>
                        item.Id != account.Id &&
                        string.Equals(item.Email, nextEmail, StringComparison.OrdinalIgnoreCase));
                    if (occupied)
                    {
                        return ProfileUpdateResult.Fail("Email is already used by another account.");
                    }
                }

                if (wantsPasswordChange && !VerifyPassword(request.CurrentPassword!, account.PasswordSalt, account.PasswordHash))
                {
                    return ProfileUpdateResult.Fail("Current password is incorrect.");
                }

                if (nextEmail is not null)
                {
                    person.Email = nextEmail;
                    account.Email = nextEmail;
                }

                if (!string.IsNullOrWhiteSpace(request.DisplayName))
                {
                    person.DisplayName = request.DisplayName.Trim();
                }

                if (request.AvatarUrl is not null)
                {
                    person.AvatarUrl = request.AvatarUrl.Trim();
                }

                if (wantsPasswordChange)
                {
                    var salt = NewToken();
                    account.PasswordSalt = salt;
                    account.PasswordHash = HashPassword(request.NewPassword!, salt);
                }

                return ProfileUpdateResult.Success(new AuthResponse(account.SessionToken, person));
            }, cancellationToken);

            return result.Response is null
                ? Results.BadRequest(new { error = result.Error ?? "Profile could not be updated." })
                : Results.Ok(result.Response);
        })
            .WithTags("Auth")
            .WithSummary("Update current profile")
            .WithDescription("Updates the authenticated user's display name, email, avatar URL, or password. Password changes require the current password and matching new password confirmation.");

        return app;
    }
}
