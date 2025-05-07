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

namespace FacebookAutoPoster
{
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

        static async Task Main(string[] args)
        {
            try
            {
                var config = new CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = true,
                    MissingFieldFound = null,  // Ignore missing fields
                    HeaderValidated = null,    // Don't validate headers
                    PrepareHeaderForMatch = args => args.Header.ToLower() // Case-insensitive header matching
                };

                using (var reader = new StreamReader("posts.csv"))
                using (var csv = new CsvReader(reader, config))
                {
                    var records = csv.GetRecords<PostData>();
                    foreach (var record in records)
                    {
                        if (string.IsNullOrWhiteSpace(record.GroupUrl))
                        {
                            Console.WriteLine("Skipping record with empty group URL");
                            continue;
                        }

                        // Sanitize profile name for directory creation
                        if (!string.IsNullOrWhiteSpace(record.ProfileName))
                        {
                            record.ProfileName = string.Join("_", record.ProfileName.Split(Path.GetInvalidFileNameChars()));
                        }
                        else
                        {
                            Console.WriteLine("Skipping record with empty profile name");
                            continue;
                        }

                        Console.WriteLine($"\nProcessing post for group: {record.GroupUrl}");
                        await PostToFacebook(record);
                        // Add random delay between posts to avoid detection
                        var random = new Random();
                        var delay = random.Next(Delays.BetweenPostsMin, Delays.BetweenPostsMax); // Random delay between 10-20 seconds
                        await Task.Delay(delay);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        static async Task PostToFacebook(PostData postData)
        {
            var options = new ChromeOptions();
            
            // Add user data directory for persistent login using profile name
            var baseProfileDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ChromeProfiles");
            if (!Directory.Exists(baseProfileDir))
            {
                Directory.CreateDirectory(baseProfileDir);
            }
            
            var userDataDir = Path.Combine(baseProfileDir, postData.ProfileName);
            if (!Directory.Exists(userDataDir))
            {
                Directory.CreateDirectory(userDataDir);
            }
            options.AddArgument($"--user-data-dir={userDataDir}");
            options.AddArgument($"--profile-directory=Default");
            
            // Enhanced anti-detection measures
            options.AddArgument("--disable-blink-features=AutomationControlled");
            options.AddArgument("--disable-notifications");
            options.AddArgument("--disable-popup-blocking");
            options.AddArgument("--disable-infobars");
            options.AddArgument("--disable-extensions");
            options.AddArgument("--disable-gpu");
            options.AddArgument("--no-sandbox");
            options.AddArgument("--disable-dev-shm-usage");
            options.AddArgument("--start-maximized");
            options.AddArgument("--disable-web-security");
            options.AddArgument("--allow-running-insecure-content");
            options.AddArgument("--disable-features=IsolateOrigins,site-per-process");
            
            // Randomize user agent
            var userAgents = new[]
            {
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
            };
            var random = new Random();
            options.AddArgument($"--user-agent={userAgents[random.Next(userAgents.Length)]}");
            
            // Enhanced experimental options
            options.AddExcludedArgument("enable-automation");
            options.AddAdditionalOption("useAutomationExtension", false);
            
            // Enhanced preferences
            var prefs = new Dictionary<string, object>
            {
                ["profile.default_content_setting_values.notifications"] = 2,
                ["credentials_enable_service"] = false,
                ["profile.password_manager_enabled"] = false,
                ["profile.managed_default_content_settings.images"] = 1,
                ["profile.default_content_setting_values.cookies"] = 1
            };
            foreach (var pref in prefs)
            {
                options.AddUserProfilePreference(pref.Key, pref.Value);
            }
            
            using (var driver = new ChromeDriver(options))
            {
                try
                {
                    // Enhanced JavaScript to prevent detection
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
                    
                    var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(60)); // Increased timeout
                    var actions = new OpenQA.Selenium.Interactions.Actions(driver); // Create actions here for use throughout

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
                        var emailInput = wait.Until(d => d.FindElement(By.Id("email")));
                        var passwordInput = driver.FindElement(By.Id("pass"));
                        var loginButton = driver.FindElement(By.Name("login"));

                        await TypeLikeHuman(emailInput, postData.Username);
                        Console.WriteLine("Waiting between login fields...");
                        await RandomDelay(Delays.LoginDelayMin, Delays.LoginDelayMax);
                        await TypeLikeHuman(passwordInput, postData.Password);
                        Console.WriteLine("Waiting before clicking login...");
                        await RandomDelay(Delays.LoginDelayMin, Delays.LoginDelayMax);
                        
                        // Move mouse to button before clicking
                        actions.MoveToElement(loginButton).Perform();
                        await RandomDelay(Delays.LoginDelayMin, Delays.LoginDelayMax);
                        loginButton.Click();

                        // Wait for login to complete with better detection
                        Console.WriteLine($"Waiting {Delays.PostLoginDelay/1000} seconds for login to complete...");
                        await Task.Delay(Delays.PostLoginDelay);
                    }
                    else
                    {
                        Console.WriteLine("Already logged in, proceeding with post...");
                    }

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

                    foreach (var selector in selectors)
                    {
                        try
                        {
                            postArea = wait.Until(d => d.FindElement(By.XPath(selector)));
                            if (postArea != null && postArea.Displayed)
                            {
                                Console.WriteLine($"Found post area using selector: {selector}");
                                break;
                            }
                        }
                        catch
                        {
                            continue;
                        }
                    }

                    if (postArea == null)
                    {
                        throw new Exception("Could not find post creation area with any known selector");
                    }

                    Console.WriteLine($"Waiting {Delays.PostAreaDelay/1000} seconds before clicking post area...");
                    await RandomDelay(Delays.PostAreaDelay, Delays.PostAreaDelay);
                    
                    // Click using JavaScript with multiple approaches
                    try
                    {
                        ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", postArea);
                    }
                    catch
                    {
                        try
                        {
                            actions.MoveToElement(postArea).Click().Perform();
                        }
                        catch
                        {
                            postArea.Click();
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

                    // Now find the actual input field within or after this area
                    IWebElement postInput = null;
                    var inputSelectors = new[]
                    {
                        "//p[@class='xdj266r x11i5rnm xat24cr x1mh8g0r x16tdsg8']",
                        "//p[contains(@class, 'xdj266r') and contains(@class, 'x11i5rnm') and contains(@class, 'xat24cr')]",
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

                    // Wait for link preview to appear and remove it
                    Console.WriteLine("Waiting for link preview to appear...");
                    await Task.Delay(5000); // Wait 5 seconds for link preview to load

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

                    // Add a final pause before clicking post
                    await RandomDelay(3000, 5000);

                    // Find and click the post button using multiple approaches
                    Console.WriteLine("Looking for post button...");
                    IWebElement postButton = null;
                    var buttonSelectors = new[]
                    {
                        "//div[@aria-label='Post']",
                        "//div[contains(@aria-label, 'Post')]",
                        "//div[contains(text(), 'Post')]",
                        "//button[contains(@aria-label, 'Post')]",
                        "//button[contains(text(), 'Post')]"
                    };

                    foreach (var selector in buttonSelectors)
                    {
                        try
                        {
                            postButton = wait.Until(d => d.FindElement(By.XPath(selector)));
                            if (postButton != null && postButton.Displayed)
                            {
                                Console.WriteLine($"Found post button using selector: {selector}");
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

                    Console.WriteLine("Post completed successfully!");
                    Console.WriteLine($"Waiting {Delays.PostCompleteDelay/1000} seconds to confirm post completion...");
                    await Task.Delay(Delays.PostCompleteDelay); // Wait to see the post being created
                    
                    // Close the browser after successful post
                    driver.Quit();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error during posting: {ex.Message}");
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                    driver.Quit();
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
    }
}
