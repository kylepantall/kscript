<h1>KScript is a developing project.</h1>

KScript allows the parsing of a XML KScript document into a functional scripting language.
An example of a KScript document:

<hr />

<h2>Script Example</h2>
<p>For additional information, include: print_info="yes" attribute within the <kscript> document open tag.</p>

<hr />

```xml
<kscript dyanmic_defs="yes">
  <!-- By default KScript allows Dynamic defs, meaning they'll be defined automatically if they haven't already upon usage -->
  <!-- <def id="entered_name"/> -->
  
  <!-- Obtains input with the prompt "What's your name?" and stores the text input to variable 'entered_name' -->
  <input to="entered_name" type="text">What's your name?</input>
  
  <!-- Outputs 'entered_name' variable to the console -->
  <echo>Hello, $entered_name!</echo>

</kscript>
```

<hr />

<p>In order to parse XML KScript documents, use the following CMD command:</p>
<strong>&lt;KScript.exe&gt; "File Location"</strong>

Replacing &lt;KScript.exe&gt; with the KScript.exe file location and "File location" with the full file path of the .XML KScript file.

<h2>Additional Information</h2>
Further documentation is still within development. 
