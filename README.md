KScript is a developing project.

KScript allows the parsing of a XML KScript document into a functional scripting language.
An example of a KScript document:


##########################################################################################################################
SCRIPT EXAMPLE

For additional information, include: print_info="yes" attribute within the <kscript> document open tag.
##########################################################################################################################

<kscript>
  <!-- Declares a variable called 'entered_name' -->
  <def id="entered_name"/>
  
  <!-- Obtains input with the prompt "What's your name?" and stores the text input to variable 'entered_name' -->
  <input to="entered_name" type="text">What's your name?</input>
  
  <!-- Outputs 'entered_name' variable to the console -->
  <echo>Hello, $entered_name!</echo>

</kscript>

##########################################################################################################################

In order to parse XML KScript documents, use the following CMD command:

<KScript.exe> "File Location"

Replacing <KScript.exe> with the KScript.exe file location and "File location" with the full file path of the .XML KScript file.

##########################################################################################################################

Further documentation is still within development. 
