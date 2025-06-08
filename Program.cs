using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using FacebookAutoPoster.Models;
using System.Threading;
using System.Linq;
using OpenQA.Selenium.Support.Extensions;
using System.Net.Http;
using System.Net;

namespace FacebookAutoPoster
{
    // Proxy configuration class
    public class ProxyConfig
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool IsValid { get; set; }
        public DateTime LastUsed { get; set; }
        public int FailCount { get; set; }
        public string AssociatedAccount { get; set; } = string.Empty;

        public string ToProxyString()
        {
            if (string.IsNullOrEmpty(Username) || string.IsNullOrEmpty(Password))
                return $"{Host}:{Port}";
            return $"{Username}:{Password}@{Host}:{Port}";
        }
    }

    class Program
    {
        // Delay settings (in milliseconds)
        private static class Delays
        {
            public static readonly int BetweenPostsMin = 10000;  // 10 seconds
            public static readonly int BetweenPostsMax = 20000;  // 20 seconds
            public static readonly int InitialDelayMin = 1000;   // 1 second
            public static readonly int InitialDelayMax = 2000;   // 2 seconds
            public static readonly int LoginDelayMin = 500;      // 0.5 seconds
            public static readonly int LoginDelayMax = 1000;     // 1 second
            public static readonly int PostLoginDelay = 5000;    // 5 seconds
            public static readonly int GroupLoadDelay = 5000;    // 5 seconds
            public static readonly int ClickDelayMin = 1000;     // 1 second
            public static readonly int ClickDelayMax = 2000;     // 2 seconds
            public static readonly int PostAreaDelay = 2000;     // 2 seconds
            public static readonly int TypeDelayMin = 20;        // 20ms
            public static readonly int TypeDelayMax = 50;        // 50ms
            public static readonly int ThinkingDelayMin = 200;   // 200ms
            public static readonly int ThinkingDelayMax = 500;   // 500ms
            public static readonly int PostCompleteDelay = 5000; // 5 seconds
        }

        // Proxy rotation settings
        private static class ProxySettings
        {
            public static readonly int MaxFailures = 3;
            public static readonly int ProxyTimeout = 30; // seconds
            public static readonly int ValidationTimeout = 10; // seconds
        }

        private static Dictionary<string, ProxyConfig> _accountProxies = new Dictionary<string, ProxyConfig>();
        private static int _postCount = 0;
        private static readonly object _proxyLock = new object();
        private static Dictionary<string, ChromeDriver> _activeDrivers = new Dictionary<string, ChromeDriver>();
        private static readonly object _driverLock = new object();

        static async Task Main(string[] args)
        {
            try
            {
                await RunAutoPoster();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
        }

        static void LoadProxies()
        {
            try
            {
                if (File.Exists("proxies.txt"))
                {
                    var lines = File.ReadAllLines("proxies.txt");
                    foreach (var line in lines)
                    {
                        if (line.Trim().StartsWith("#") || string.IsNullOrWhiteSpace(line))
                            continue;

                        var parts = line.Split(':');
                        if (parts.Length >= 3) // Now expecting format: account:host:port or account:host:port:username:password
                        {
                            var account = parts[0];
                            var proxy = new ProxyConfig
                            {
                                Host = parts[1],
                                Port = int.Parse(parts[2]),
                                IsValid = true,
                                LastUsed = DateTime.MinValue,
                                FailCount = 0,
                                AssociatedAccount = account
                            };

                            // If username and password are provided
                            if (parts.Length >= 5)
                            {
                                proxy.Username = parts[3];
                                proxy.Password = parts[4];
                            }

                            _accountProxies[account] = proxy;
                            Console.WriteLine($"Loaded proxy for account: {account}");
                        }
                    }
                    Console.WriteLine($"Loaded {_accountProxies.Count} account-proxy pairs");
                }
                else
                {
                    Console.WriteLine("No proxies.txt file found. Running without proxies.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading proxies: {ex.Message}");
            }
        }

        static async Task<ProxyConfig> GetProxyForAccount(string account)
        {
            lock (_proxyLock)
            {
                if (_accountProxies.TryGetValue(account, out var proxy))
                {
                    if (proxy.IsValid && proxy.FailCount < ProxySettings.MaxFailures)
                    {
                        proxy.LastUsed = DateTime.Now;
                        return proxy;
                    }
                    else
                    {
                        Console.WriteLine($"Warning: No valid proxy found for account {account}");
                        return null;
                    }
                }
                Console.WriteLine($"Warning: No proxy configured for account {account}");
                return null;
            }
        }

        static async Task<bool> ValidateProxy(ProxyConfig proxy)
        {
            try
            {
                using (var handler = new HttpClientHandler
                {
                    Proxy = new WebProxy(proxy.ToProxyString()),
                    UseProxy = true,
                    ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
                })
                using (var client = new HttpClient(handler))
                {
                    client.Timeout = TimeSpan.FromSeconds(ProxySettings.ValidationTimeout);
                    var response = await client.GetAsync("https://www.facebook.com");
                    return response.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false;
            }
        }

        static void MarkProxyAsFailed(ProxyConfig proxy)
        {
            lock (_proxyLock)
            {
                proxy.FailCount++;
                if (proxy.FailCount >= ProxySettings.MaxFailures)
                {
                    proxy.IsValid = false;
                    Console.WriteLine($"Proxy for account {proxy.AssociatedAccount} ({proxy.Host}:{proxy.Port}) marked as invalid after {proxy.FailCount} failures");
                }
            }
        }

        private static string GetLogFileName()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"post_log_{DateTime.Now:yyyyMMdd}.csv");
        }

        private static async Task LogPostResult(PostData postData, bool success, string message = "")
        {
            try
            {
                var logFile = GetLogFileName();
                var logEntry = new
                {
                    DateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    ProfileName = postData.ProfileName,
                    GroupUrl = postData.GroupUrl,
                    Status = success ? "Success" : "Failed",
                    Message = message,
                    PostPreview = postData.PostText.Length > 50 ? postData.PostText.Substring(0, 50) + "..." : postData.PostText
                };

                bool fileExists = File.Exists(logFile);
                using (var writer = new StreamWriter(logFile, true))
                using (var csv = new CsvWriter(writer, new CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture)))
                {
                    if (!fileExists)
                    {
                        csv.WriteHeader(logEntry.GetType());
                        csv.NextRecord();
                    }
                    csv.WriteRecord(logEntry);
                    csv.NextRecord();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error writing to log file: {ex.Message}");
            }
        }

        static void CleanupChromeProfiles()
        {
            try
            {
                var baseProfileDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ChromeProfiles");
                if (Directory.Exists(baseProfileDir))
                {
                    Console.WriteLine("Cleaning up Chrome profiles...");
                    Directory.Delete(baseProfileDir, true);
                    Console.WriteLine("Chrome profiles cleaned up successfully.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error cleaning up Chrome profiles: {ex.Message}");
            }
        }

        static async Task PostToFacebook(PostData postData)
        {
            ChromeDriver? driver = null;
            bool isNewDriver = false;
            ProxyConfig? proxy = null;

            try
            {
                // Check if we already have a driver for this profile
                lock (_driverLock)
                {
                    _activeDrivers.TryGetValue(postData.ProfileName, out driver);
                }

                if (driver == null)
                {
                    Console.WriteLine($"Creating new Chrome instance for profile: {postData.ProfileName}");
                    var options = new ChromeOptions();
                    
                    // Get proxy for this specific account
                    proxy = await GetProxyForAccount(postData.ProfileName);
                    if (proxy != null)
                    {
                        Console.WriteLine($"Using proxy for account {postData.ProfileName}: {proxy.Host}:{proxy.Port}");
                        if (await ValidateProxy(proxy))
                        {
                            options.AddArgument($"--proxy-server={proxy.ToProxyString()}");
                        }
                        else
                        {
                            Console.WriteLine($"Proxy for account {postData.ProfileName} failed validation");
                            MarkProxyAsFailed(proxy);
                        }
                    }
                    
                    // Add user data directory for persistent login using profile name
                    var baseProfileDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ChromeProfiles");
                    if (!Directory.Exists(baseProfileDir))
                    {
                        Directory.CreateDirectory(baseProfileDir);
                    }
                    
                    // Use fixed profile directory for each account
                    var userDataDir = Path.Combine(baseProfileDir, postData.ProfileName);
                    if (!Directory.Exists(userDataDir))
                    {
                        Directory.CreateDirectory(userDataDir);
                    }
                    options.AddArgument($"--user-data-dir={userDataDir}");
                    options.AddArgument($"--profile-directory=Default");
                    
                    // Essential anti-detection options
                    options.AddArgument("--disable-blink-features=AutomationControlled");
                    options.AddArgument("--disable-notifications");
                    options.AddArgument("--start-maximized");
                    
                    // Add remote debugging port
                    var debugPort = new Random().Next(9222, 9999);
                    options.AddArgument($"--remote-debugging-port={debugPort}");

                    // Add these options to prevent automation detection
                    options.AddExcludedArgument("enable-automation");
                    options.AddAdditionalOption("useAutomationExtension", false);
                    
                    try
                    {
                        driver = new ChromeDriver(options);
                        lock (_driverLock)
                        {
                            _activeDrivers[postData.ProfileName] = driver;
                        }
                        isNewDriver = true;

                        // Execute JavaScript to remove automation flags
                        ((IJavaScriptExecutor)driver).ExecuteScript(@"
                            Object.defineProperty(navigator, 'webdriver', {
                                get: () => undefined
                            });
                            Object.defineProperty(navigator, 'plugins', {
                                get: () => [1, 2, 3, 4, 5]
                            });
                            Object.defineProperty(navigator, 'languages', {
                                get: () => ['en-US', 'en']
                            });
                            window.chrome = {
                                runtime: {},
                                loadTimes: function(){},
                                csi: function(){},
                                app: {}
                            };
                        ");

                        Console.WriteLine("Opening Facebook...");
                        driver.Navigate().GoToUrl("https://www.facebook.com");
                        
                        var initialWait = new WebDriverWait(driver, TimeSpan.FromSeconds(60));
                        var initialActions = new OpenQA.Selenium.Interactions.Actions(driver);

                        // Add random delay to simulate human behavior
                        Console.WriteLine("Initial delay before starting...");
                        await RandomDelay(Delays.InitialDelayMin, Delays.InitialDelayMax);

                        // Check if we need to login by looking for the email input field
                        bool needsLogin = false;
                        try
                        {
                            var emailField = driver.FindElements(By.Id("email"));
                            needsLogin = emailField.Count > 0 && emailField[0].Displayed;
                        }
                        catch
                        {
                            needsLogin = false;
                        }

                        if (needsLogin)
                        {
                            // Login with enhanced human-like behavior
                            Console.WriteLine("Login required. Attempting to login...");
                            var emailInput = initialWait.Until(d => d.FindElement(By.Id("email")));
                            var passwordInput = driver.FindElement(By.Id("pass"));
                            var loginButton = driver.FindElement(By.Name("login"));

                            await TypeLikeHuman(emailInput, postData.Username);
                            Console.WriteLine("Waiting between login fields...");
                            await RandomDelay(Delays.LoginDelayMin, Delays.LoginDelayMax);
                            await TypeLikeHuman(passwordInput, postData.Password);
                            Console.WriteLine("Waiting before clicking login...");
                            await RandomDelay(Delays.LoginDelayMin, Delays.LoginDelayMax);
                            
                            initialActions.MoveToElement(loginButton).Perform();
                            await RandomDelay(Delays.LoginDelayMin, Delays.LoginDelayMax);
                            loginButton.Click();

                            Console.WriteLine($"Waiting {Delays.PostLoginDelay/1000} seconds for login to complete...");
                            await Task.Delay(Delays.PostLoginDelay);

                            // Check for 2FA
                            try
                            {
                                Console.WriteLine("Checking for 2FA...");
                                var wait2FA = new WebDriverWait(driver, TimeSpan.FromSeconds(60)); // Increased timeout
                                var twoFactorElements = driver.FindElements(By.XPath("//input[@placeholder='Enter code']"));
                                
                                if (twoFactorElements.Count > 0 && twoFactorElements[0].Displayed)
                                {
                                    Console.WriteLine("2FA detected! Please enter the code manually...");
                                    Console.WriteLine("Waiting for 2FA completion...");
                                    
                                    // Wait for 2FA to be completed (check if we're redirected to Facebook home)
                                    wait2FA.Until(d => d.Url.Contains("facebook.com") && !d.Url.Contains("checkpoint"));
                                    Console.WriteLine("2FA completed successfully!");
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"No 2FA detected or error checking 2FA: {ex.Message}");
                            }

                            // Additional wait after login/2FA
                            Console.WriteLine("Waiting additional time for login process to complete...");
                            await Task.Delay(30000); // Increased to 30 seconds after login/2FA
                            
                            // Additional check to ensure we're properly logged in
                            try
                            {
                                Console.WriteLine("Verifying login status...");
                                driver.Navigate().GoToUrl("https://www.facebook.com");
                                await Task.Delay(10000); // Wait 10 seconds after navigation
                                
                                // Check if we're still on login page
                                var emailField = driver.FindElements(By.Id("email"));
                                if (emailField.Count > 0 && emailField[0].Displayed)
                                {
                                    Console.WriteLine("Still on login page, waiting additional time...");
                                    await Task.Delay(20000); // Wait 20 more seconds if still on login page
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error verifying login status: {ex.Message}");
                            }
                        }
                        else
                        {
                            Console.WriteLine("Already logged in, proceeding with post...");
                        }
                    }
                    catch (Exception ex) when (ex.Message.Contains("user data directory is already in use"))
                    {
                        Console.WriteLine("Chrome profile is in use, cleaning up...");
                        CleanupChromeProfiles();
                        driver = new ChromeDriver(options);
                        lock (_driverLock)
                        {
                            _activeDrivers[postData.ProfileName] = driver;
                        }
                        isNewDriver = true;
                    }
                }

                if (driver == null)
                {
                    throw new Exception("Failed to initialize Chrome driver");
                }

                var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(60));
                var actions = new OpenQA.Selenium.Interactions.Actions(driver);

                // Navigate to the group
                Console.WriteLine($"Navigating to group: {postData.GroupUrl}");
                driver.Navigate().GoToUrl(postData.GroupUrl);
                Console.WriteLine($"Waiting {Delays.GroupLoadDelay/1000} seconds for group to load...");
                await Task.Delay(Delays.GroupLoadDelay);

                // Simulate human-like scrolling and interaction
                Console.WriteLine("Simulating human-like behavior...");
                try
                {
                    // Random initial scroll
                    var scrollRandom = new Random();
                    var scrollAmount = scrollRandom.Next(300, 800);
                    ((IJavaScriptExecutor)driver).ExecuteScript($"window.scrollBy(0, {scrollAmount});");
                    await RandomDelay(1000, 2000);

                    // Scroll up a bit
                    scrollAmount = scrollRandom.Next(100, 300);
                    ((IJavaScriptExecutor)driver).ExecuteScript($"window.scrollBy(0, -{scrollAmount});");
                    await RandomDelay(800, 1500);

                    // Random mouse movements
                    var elements = driver.FindElements(By.CssSelector("div[role='article']"));
                    if (elements.Count > 0)
                    {
                        // Move to a random post
                        var randomPost = elements[scrollRandom.Next(0, Math.Min(3, elements.Count))];
                        actions.MoveToElement(randomPost).Perform();
                        await RandomDelay(500, 1000);

                        // Move away
                        actions.MoveByOffset(scrollRandom.Next(-100, 100), scrollRandom.Next(-100, 100)).Perform();
                        await RandomDelay(500, 1000);
                    }

                    // Scroll to top
                    ((IJavaScriptExecutor)driver).ExecuteScript("window.scrollTo(0, 0);");
                    await RandomDelay(1000, 2000);

                    // Find and scroll to post creation area
                    var postAreaElement = wait.Until(d => d.FindElement(By.XPath("//span[contains(text(), 'Write something...')]")));
                    if (postAreaElement != null)
                    {
                        // Scroll to post area with smooth behavior
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView({behavior: 'smooth', block: 'center'});", postAreaElement);
                        await RandomDelay(1500, 2500);

                        // Move mouse to post area with slight offset
                        actions.MoveToElement(postAreaElement, scrollRandom.Next(-10, 10), scrollRandom.Next(-10, 10)).Perform();
                        await RandomDelay(800, 1500);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error during human-like behavior simulation: {ex.Message}");
                }

                // Try multiple approaches to find the post creation area
                Console.WriteLine("Looking for post creation area...");
                IWebElement postArea = null;
                var selectors = new[]
                {
                    "//span[contains(text(), 'Write something...')]",
                    "//span[contains(@class, 'Write something...')]",
                    "//div[contains(@class, 'Write something...')]",
                    "//div[@role='button' and contains(@aria-label, 'Create Post')]",
                    "//div[contains(@aria-label, 'Create Post')]",
                    "//div[contains(@aria-label, 'Write Post')]"
                };

                Console.WriteLine($"Trying {selectors.Length} different selectors to find post area...");
                foreach (var selector in selectors)
                {
                    try
                    {
                        Console.WriteLine($"Attempting selector: {selector}");
                        postArea = wait.Until(d => d.FindElement(By.XPath(selector)));
                        if (postArea != null && postArea.Displayed)
                        {
                            Console.WriteLine($"Successfully found post area using selector: {selector}");
                            Console.WriteLine($"Post area is displayed: {postArea.Displayed}");
                            Console.WriteLine($"Post area is enabled: {postArea.Enabled}");
                            break;
                        }
                        else
                        {
                            Console.WriteLine($"Found element but it's not displayed or enabled");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed with selector {selector}: {ex.Message}");
                        continue;
                    }
                }

                if (postArea == null)
                {
                    Console.WriteLine("ERROR: Could not find post creation area with any known selector");
                    // Take a screenshot for debugging
                    try
                    {
                        var screenshot = ((ITakesScreenshot)driver).GetScreenshot();
                        var screenshotPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "post_area_not_found.png");
                        screenshot.SaveAsFile(screenshotPath);
                        Console.WriteLine($"Screenshot saved to: {screenshotPath}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to take screenshot: {ex.Message}");
                    }
                    throw new Exception("Could not find post creation area with any known selector");
                }

                Console.WriteLine($"Waiting {Delays.PostAreaDelay/1000} seconds before clicking post area...");
                await RandomDelay(Delays.PostAreaDelay, Delays.PostAreaDelay);
                
                // Click using JavaScript with multiple approaches
                Console.WriteLine("Attempting to click post area...");
                try
                {
                    Console.WriteLine("Trying JavaScript click...");
                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", postArea);
                    Console.WriteLine("JavaScript click successful");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"JavaScript click failed: {ex.Message}");
                    try
                    {
                        Console.WriteLine("Trying Actions click...");
                        actions.MoveToElement(postArea).Click().Perform();
                        Console.WriteLine("Actions click successful");
                    }
                    catch (Exception ex2)
                    {
                        Console.WriteLine($"Actions click failed: {ex2.Message}");
                        try
                        {
                            Console.WriteLine("Trying direct click...");
                            postArea.Click();
                            Console.WriteLine("Direct click successful");
                        }
                        catch (Exception ex3)
                        {
                            Console.WriteLine($"All click attempts failed. Last error: {ex3.Message}");
                            throw;
                        }
                    }
                }

                await Task.Delay(Delays.PostAreaDelay);
                Console.WriteLine("Waiting for post input field...");

                // Find the input field in the popup using multiple approaches
                Console.WriteLine("Looking for post input field in popup...");
                
                // First find the post creation area
                var postCreationArea = wait.Until(d => d.FindElement(By.XPath("//div[contains(text(), 'Create a public post…')]")));
                Console.WriteLine("Found post creation area");
                await RandomDelay(1000, 2000);
                Console.WriteLine("Waited post creation area");

                // Click the anonymous post toggle if IsAnonymous is true
                if (postData.IsAnonymous)
                {
                    try
                    {
                        Console.WriteLine("Attempting to enable anonymous posting...");
                        var anonymousToggle = wait.Until(d => d.FindElement(By.XPath("//input[@aria-label='Anonymous post toggle']")));
                        if (anonymousToggle != null && anonymousToggle.Displayed)
                        {
                            // Check if it's not already checked
                            if (!anonymousToggle.Selected)
                            {
                                await RandomDelay(1000, 2000);
                                try
                                {
                                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", anonymousToggle);
                                }
                                catch
                                {
                                    actions.MoveToElement(anonymousToggle).Click().Perform();
                                }
                                Console.WriteLine("Anonymous posting enabled successfully");
                                await RandomDelay(2000, 3000);

                                // Handle the "Got it" confirmation dialog
                                try
                                {
                                    Console.WriteLine("Looking for 'Got it' confirmation dialog...");
                                    var gotItButton = wait.Until(d => d.FindElement(By.XPath("//span[contains(text(), 'Got it')]")));
                                    if (gotItButton != null && gotItButton.Displayed)
                                    {
                                        await RandomDelay(1000, 2000);
                                        try
                                        {
                                            // Try to find the parent div that's clickable
                                            var parentDiv = gotItButton.FindElement(By.XPath("./ancestor::div[@role='none']"));
                                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", parentDiv);
                                        }
                                        catch
                                        {
                                            try
                                            {
                                                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", gotItButton);
                                            }
                                            catch
                                            {
                                                actions.MoveToElement(gotItButton).Click().Perform();
                                            }
                                        }
                                        Console.WriteLine("Clicked 'Got it' confirmation");
                                        await RandomDelay(2000, 3000);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error handling 'Got it' confirmation: {ex.Message}");
                                }
                            }
                            else
                            {
                                Console.WriteLine("Anonymous posting already enabled");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error enabling anonymous posting: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine("Anonymous posting disabled for this post");
                }

                // Now find the actual input field within or after this area
                IWebElement postInput = null;
                var inputSelectors = new[]
                {
                    "//p[@class='xdj266r x14z9mp xat24cr x1lziwak x16tdsg8']",
                    "//p[contains(@class, 'xdj266r') and contains(@class, 'x14z9mp') and contains(@class, 'xat24cr')]",
                    "//div[@contenteditable='true']//p[contains(@class, 'xdj266r')]",
                    "//div[@role='textbox']//p[contains(@class, 'xdj266r')]",
                    "//div[contains(@class, 'notranslate')]//p[contains(@class, 'xdj266r')]"
                };

                Console.WriteLine("Attempting to find post input field in popup...");
                foreach (var selector in inputSelectors)
                {                             
                    Console.WriteLine($"Trying selector: {selector}");
                    try
                    {
                        // Wait for the popup to be fully loaded
                        await Task.Delay(2000);
                        postInput = wait.Until(d => d.FindElement(By.XPath(selector)));
                        if (postInput != null && postInput.Displayed)
                        {
                            Console.WriteLine($"Found input field using selector: {selector}");
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error finding selector {selector}: {ex.Message}");
                        continue;
                    }
                }

                if (postInput == null)
                {
                    // Take a screenshot for debugging
                    try
                    {
                        var screenshot = ((ITakesScreenshot)driver).GetScreenshot();
                        var screenshotPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error_screenshot.png");
                        screenshot.SaveAsFile(screenshotPath);
                        Console.WriteLine($"Screenshot saved to: {screenshotPath}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to take screenshot: {ex.Message}");
                    }
                    throw new Exception("Could not find post input field with any known selector");
                }

                await RandomDelay(1000, 2000);

                // Try to focus the input field before typing
                try
                {
                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].focus();", postInput);
                    await RandomDelay(500, 1000);
                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", postInput);
                }
                catch
                {
                    actions.MoveToElement(postInput).Click().Perform();
                }
                await RandomDelay(500, 1000);

                // Enter post text with enhanced human-like behavior
                Console.WriteLine("Entering post text...");
                
                // Split the text into parts and add some natural pauses
                var textParts = postData.PostText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var currentPart = "";
                
                foreach (var part in textParts)
                {
                    currentPart += part + " ";
                    await TypeLikeHuman(postInput, part + " ");
                    
                    // Add longer pauses between sentences or after certain words
                    if (part.EndsWith(".") || part.EndsWith("!") || part.EndsWith("?"))
                    {
                        await RandomDelay(1000, 2000);
                    }
                    else if (part.Contains("http") || part.Contains("www"))
                    {
                        // Add extra delay before and after typing URLs
                        await RandomDelay(2000, 3000);
                    }
                    else
                    {
                        await RandomDelay(300, 800);
                    }
                }

                // Add a final pause before proceeding
                await RandomDelay(2000, 3000);

                // Wait for link preview to appear and remove it if ClosePreview is true
                Console.WriteLine("Waiting for link preview to appear...");
                await Task.Delay(5000); // Wait 5 seconds for link preview to load

                if (postData.ClosePreview)
                {
                    try
                    {
                        var removeLinkButton = wait.Until(d => d.FindElement(By.XPath("//div[@aria-label='Remove link preview from your post']")));
                        if (removeLinkButton != null && removeLinkButton.Displayed)
                        {
                            Console.WriteLine("Found link preview, removing it...");
                            await RandomDelay(1000, 2000);
                            try
                            {
                                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", removeLinkButton);
                            }
                            catch
                            {
                                actions.MoveToElement(removeLinkButton).Click().Perform();
                            }
                            Console.WriteLine("Link preview removed successfully");
                            await RandomDelay(2000, 3000); // Longer delay after removing preview
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"No link preview found or error removing it: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine("Keeping link preview as per configuration");
                }

                // Add a final pause before clicking post
                await RandomDelay(3000, 5000);

                // Find and click the post button using multiple approaches
                Console.WriteLine("Looking for post button...");
                IWebElement postButton = null;
                var buttonSelectors = postData.IsAnonymous ? 
                    new[] {
                        // Anonymous post button selector
                        "//div[@role='none' and contains(@class, 'x1ja2u2z') and contains(@class, 'x78zum5')]//span[contains(@class, 'x1lliihq') and contains(@class, 'x6ikm8r') and contains(text(), 'Submit')]"
                    } : 
                    new[] {
                        // Non-anonymous post button selector
                        "//div[contains(@class, 'x1ja2u2z') and contains(@class, 'x78zum5')]//span[contains(text(), 'Post')]"
                    };

                Console.WriteLine($"Looking for {(postData.IsAnonymous ? "Submit" : "Post")} button...");
                foreach (var selector in buttonSelectors)
                {
                    try
                    {
                        postButton = wait.Until(d => d.FindElement(By.XPath(selector)));
                        if (postButton != null && postButton.Displayed)
                        {
                            Console.WriteLine($"Found {(postData.IsAnonymous ? "Submit" : "Post")} button using selector: {selector}");
                            break;
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }

                if (postButton == null)
                {
                    throw new Exception("Could not find post button with any known selector");
                }

                Console.WriteLine("Waiting before clicking post button...");
                await RandomDelay(Delays.PostAreaDelay, Delays.PostAreaDelay);

                // Click post button with multiple approaches
                try
                {
                    // Try to find the parent div that's clickable
                    var parentDiv = postButton.FindElement(By.XPath("./ancestor::div[@role='none']"));
                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", parentDiv);
                }
                catch
                {
                    try
                    {
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", postButton);
                    }
                    catch
                    {
                        try
                        {
                            actions.MoveToElement(postButton).Click().Perform();
                        }
                        catch
                        {
                            postButton.Click();
                        }
                    }
                }

                Console.WriteLine("Post completed successfully!");
                await LogPostResult(postData, true, "Post completed successfully");
                Console.WriteLine($"Waiting {Delays.PostCompleteDelay/1000} seconds before next post...");
                await Task.Delay(Delays.PostCompleteDelay);

                // Close the current post dialog if it's still open
                try
                {
                    var closeButton = driver.FindElements(By.XPath("//div[@aria-label='Close']"));
                    if (closeButton.Count > 0 && closeButton[0].Displayed)
                    {
                        closeButton[0].Click();
                        await Task.Delay(2000);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error closing post dialog: {ex.Message}");
                }

                // Don't navigate here - let RunAutoPoster handle the next group navigation
            }
            catch (Exception ex)
            {
                if (proxy != null)
                {
                    MarkProxyAsFailed(proxy);
                }
                var errorMessage = $"Error during posting: {ex.Message}";
                Console.WriteLine(errorMessage);
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                await LogPostResult(postData, false, errorMessage);

                // Only quit the driver if it's a new one and there was an error
                if (isNewDriver && driver != null)
                {
                    driver.Quit();
                    lock (_driverLock)
                    {
                        _activeDrivers.Remove(postData.ProfileName);
                    }
                }
            }
        }

        static async Task TypeLikeHuman(IWebElement element, string text)
        {
            var random = new Random();
            foreach (char c in text)
            {
                element.SendKeys(c.ToString());
                // Random delay between keystrokes (50-150ms)
                await Task.Delay(random.Next(Delays.TypeDelayMin, Delays.TypeDelayMax));
                
                // Occasionally add a longer pause to simulate thinking
                if (random.Next(100) < 5) // 5% chance
                {
                    await Task.Delay(random.Next(Delays.ThinkingDelayMin, Delays.ThinkingDelayMax));
                }
            }
        }

        static async Task RandomDelay(int minMs, int maxMs)
        {
            var random = new Random();
            var delay = random.Next(minMs, maxMs);
            await Task.Delay(delay);
        }

        static async Task RunAutoPoster()
        {
            Console.WriteLine($"Starting auto poster at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"Log file will be created at: {GetLogFileName()}");

            // Load proxies
            LoadProxies();

            var config = new CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                MissingFieldFound = null,
                HeaderValidated = null,
                PrepareHeaderForMatch = args => args.Header.ToLower()
            };

            try
            {
                using (var reader = new StreamReader("posts.csv"))
                using (var csv = new CsvReader(reader, config))
                {
                    var records = csv.GetRecords<PostData>().ToList();
                    Console.WriteLine($"Found {records.Count} posts to process");

                    // Group records by ProfileName
                    var groupedRecords = records.GroupBy(r => r.ProfileName);

                    foreach (var group in groupedRecords)
                    {
                        Console.WriteLine($"\nProcessing posts for profile: {group.Key}");
                        ChromeDriver? driver = null;
                        bool isNewDriver = false;
                        var groupList = group.ToList();

                        try
                        {
                            for (int i = 0; i < groupList.Count; i++)
                            {
                                var record = groupList[i];
                                if (string.IsNullOrWhiteSpace(record.GroupUrl))
                                {
                                    Console.WriteLine("Skipping record with empty group URL");
                                    await LogPostResult(record, false, "Empty group URL");
                                    continue;
                                }

                                if (!string.IsNullOrWhiteSpace(record.ProfileName))
                                {
                                    record.ProfileName = string.Join("_", record.ProfileName.Split(Path.GetInvalidFileNameChars()));
                                }
                                else
                                {
                                    Console.WriteLine("Skipping record with empty profile name");
                                    await LogPostResult(record, false, "Empty profile name");
                                    continue;
                                }

                                Console.WriteLine($"Processing post for group: {record.GroupUrl}");
                                
                                // If this is not the first post, navigate to the next group
                                if (driver != null && !isNewDriver && i > 0)
                                {
                                    var nextGroupUrl = record.GroupUrl;
                                    Console.WriteLine($"Navigating to next group: {nextGroupUrl}");
                                    driver.Navigate().GoToUrl(nextGroupUrl);
                                    Console.WriteLine($"Waiting {Delays.GroupLoadDelay/1000} seconds for group to load...");
                                    await Task.Delay(Delays.GroupLoadDelay);

                                    // Simulate human-like scrolling and interaction for the new group
                                    Console.WriteLine("Simulating human-like behavior for new group...");
                                    try
                                    {
                                        var actions = new OpenQA.Selenium.Interactions.Actions(driver);
                                        // Random initial scroll
                                        var scrollRandom = new Random();
                                        var scrollAmount = scrollRandom.Next(300, 800);
                                        ((IJavaScriptExecutor)driver).ExecuteScript($"window.scrollBy(0, {scrollAmount});");
                                        await RandomDelay(1000, 2000);

                                        // Scroll up a bit
                                        scrollAmount = scrollRandom.Next(100, 300);
                                        ((IJavaScriptExecutor)driver).ExecuteScript($"window.scrollBy(0, -{scrollAmount});");
                                        await RandomDelay(800, 1500);

                                        // Random mouse movements
                                        var elements = driver.FindElements(By.CssSelector("div[role='article']"));
                                        if (elements.Count > 0)
                                        {
                                            // Move to a random post
                                            var randomPost = elements[scrollRandom.Next(0, Math.Min(3, elements.Count))];
                                            actions.MoveToElement(randomPost).Perform();
                                            await RandomDelay(500, 1000);

                                            // Move away
                                            actions.MoveByOffset(scrollRandom.Next(-100, 100), scrollRandom.Next(-100, 100)).Perform();
                                            await RandomDelay(500, 1000);
                                        }

                                        // Scroll to top
                                        ((IJavaScriptExecutor)driver).ExecuteScript("window.scrollTo(0, 0);");
                                        await RandomDelay(1000, 2000);
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"Error during human-like behavior simulation: {ex.Message}");
                                    }
                                }

                                await PostToFacebook(record);
                                
                                var random = new Random();
                                var delay = random.Next(Delays.BetweenPostsMin, Delays.BetweenPostsMax);
                                Console.WriteLine($"Waiting {delay/1000} seconds before next post...");
                                await Task.Delay(delay);
                            }
                        }
                        finally
                        {
                            if (driver != null)
                            {
                                try
                                {
                                    Console.WriteLine("Closing Chrome instance...");
                                    driver.Quit();
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"Error closing Chrome instance: {ex.Message}");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RunAutoPoster: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }

            Console.WriteLine($"\nAuto poster completed at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine($"Log file created at: {GetLogFileName()}");
        }
    }
}
