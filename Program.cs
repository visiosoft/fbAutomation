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

namespace FacebookAutoPoster
{
    class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
                var config = new CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = true,
                };

                using (var reader = new StreamReader("posts.csv"))
                using (var csv = new CsvReader(reader, config))
                {
                    var records = csv.GetRecords<PostData>();
                    foreach (var record in records)
                    {
                        Console.WriteLine($"\nProcessing post for group: {record.GroupUrl}");
                        await PostToFacebook(record);
                        // Add random delay between posts to avoid detection
                        var random = new Random();
                        var delay = random.Next(30000, 60000); // Random delay between 30-60 seconds
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
                    await RandomDelay(3000, 6000);

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
                        await RandomDelay(1000, 2000);
                        await TypeLikeHuman(passwordInput, postData.Password);
                        await RandomDelay(1000, 2000);
                        
                        // Move mouse to button before clicking
                        actions.MoveToElement(loginButton).Perform();
                        await RandomDelay(500, 1000);
                        loginButton.Click();

                        // Wait for login to complete with better detection
                        Console.WriteLine("Waiting for login to complete...");
                        await Task.Delay(20000);
                    }
                    else
                    {
                        Console.WriteLine("Already logged in, proceeding with post...");
                    }

                    // Navigate to the group
                    Console.WriteLine($"Navigating to group: {postData.GroupUrl}");
                    driver.Navigate().GoToUrl(postData.GroupUrl);
                    await Task.Delay(15000);

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

                    await RandomDelay(2000, 4000);
                    
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

                    await Task.Delay(5000);

                    // Find the input field in the popup using multiple approaches
                    Console.WriteLine("Looking for post input field in popup...");
                    
                    // First find the post creation area
                    var postCreationArea = wait.Until(d => d.FindElement(By.XPath("//div[contains(text(), 'Create a public post…')]")));
                    Console.WriteLine("Found post creation area");
                    await RandomDelay(2000, 3000);

                    // Now find the actual input field within or after this area
                    IWebElement postInput = null;
                    var inputSelectors = new[]
                    {
                        ".//following::span[@data-lexical-text='true']",
                        ".//following::div[@contenteditable='true']//span[@data-lexical-text='true']",
                        "//span[@data-lexical-text='true']",
                        "//div[@contenteditable='true']//span[@data-lexical-text='true']",
                        "//div[@role='textbox']//span[@data-lexical-text='true']",
                        "//div[contains(@aria-label, 'Write something')]//span[@data-lexical-text='true']"
                    };

                    foreach (var selector in inputSelectors)
                    {
                        try
                        {
                            // If selector starts with .//, search from postCreationArea
                            if (selector.StartsWith(".//"))
                            {
                                postInput = postCreationArea.FindElement(By.XPath(selector));
                            }
                            else
                            {
                                postInput = wait.Until(d => d.FindElement(By.XPath(selector)));
                            }
                            
                            if (postInput != null && postInput.Displayed)
                            {
                                Console.WriteLine($"Found input field using selector: {selector}");
                                break;
                            }
                        }
                        catch
                        {
                            continue;
                        }
                    }

                    if (postInput == null)
                    {
                        // If we couldn't find the span, try to find the parent contenteditable div
                        var parentSelectors = new[]
                        {
                            ".//following::div[@contenteditable='true']",
                            ".//following::div[@role='textbox']",
                            "//div[@contenteditable='true']",
                            "//div[@role='textbox']"
                        };

                        foreach (var selector in parentSelectors)
                        {
                            try
                            {
                                if (selector.StartsWith(".//"))
                                {
                                    postInput = postCreationArea.FindElement(By.XPath(selector));
                                }
                                else
                                {
                                    postInput = wait.Until(d => d.FindElement(By.XPath(selector)));
                                }
                                
                                if (postInput != null && postInput.Displayed)
                                {
                                    Console.WriteLine($"Found parent input field using selector: {selector}");
                                    break;
                                }
                            }
                            catch
                            {
                                continue;
                            }
                        }
                    }

                    if (postInput == null)
                    {
                        throw new Exception("Could not find post input field with any known selector");
                    }

                    await RandomDelay(2000, 4000);

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
                    await RandomDelay(1000, 2000);

                    // Enter post text with enhanced human-like behavior
                    Console.WriteLine("Entering post text...");
                    await TypeLikeHuman(postInput, postData.PostText);
                    await RandomDelay(2000, 4000);

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

                    await RandomDelay(2000, 4000);

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
                    await Task.Delay(10000); // Wait to see the post being created
                    
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
                await Task.Delay(random.Next(50, 150));
                
                // Occasionally add a longer pause to simulate thinking
                if (random.Next(100) < 5) // 5% chance
                {
                    await Task.Delay(random.Next(500, 1000));
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
