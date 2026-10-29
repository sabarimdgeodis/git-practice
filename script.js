const form = document.getElementById("greeting-form");
const nameInput = document.getElementById("name");
const message = document.getElementById("message");

form.addEventListener("submit", function (event) {
  event.preventDefault();

  const name = nameInput.value.trim();

  if (!name) {
    message.textContent = "Please enter your name.";
    nameInput.focus();
    return;
  }

  message.textContent = `Hello, ${name}! Your static web app is working.`;
});