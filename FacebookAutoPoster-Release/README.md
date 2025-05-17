# Facebook Auto Poster

A tool for automating Facebook group posts with support for anonymous posting and link preview control.

## Installation

1. Download the latest release from the releases page
2. Extract the ZIP file to your desired location
3. Run `FacebookAutoPoster.exe`

## Required Files

The following files must be present in the same directory as the executable:

1. `posts.csv` - Contains your post configurations
2. `proxies.txt` (optional) - Contains your proxy configurations

### posts.csv Format

```csv
ProfileName,Username,Password,GroupUrl,PostText,IsAnonymous,ClosePreview
profile1,username1,password1,https://facebook.com/groups/group1,Your post text here,false,false
```

### proxies.txt Format

```txt
# Format: account:host:port or account:host:port:username:password
profile1:proxy1.example.com:8080
profile2:proxy2.example.com:8080:user:pass
```

## Features

- Multiple profile support
- Anonymous posting option
- Link preview control
- Proxy support
- Persistent login sessions
- Detailed logging
- Human-like behavior simulation

## System Requirements

- Windows 10 or later
- 4GB RAM minimum
- Internet connection
- Chrome browser installed

## Troubleshooting

1. If the application fails to start:
   - Ensure all required files are present
   - Check if Chrome is installed
   - Verify your internet connection

2. If posting fails:
   - Check your login credentials
   - Verify group URLs are correct
   - Check proxy configurations if using proxies
   - Review the log file for detailed error messages

## Support

For issues and feature requests, please create an issue in the repository.

## Important Notes

- Make sure your Facebook account has 2FA disabled or use an app password
- The application waits 30 seconds between posts to avoid triggering Facebook's spam detection
- You can enable headless mode by uncommenting the relevant line in Program.cs
- Keep your credentials secure and never share your posts.csv file

## Running the Application

1. Open a terminal in the project directory
2. Run the following command:
   ```
   dotnet run
   ```

## Troubleshooting

If you encounter any issues:
1. Make sure ChromeDriver version matches your Chrome browser version
2. Check your internet connection
3. Verify that your Facebook credentials are correct
4. Ensure you have permission to post in the target groups
