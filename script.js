const form = document.getElementById("trailer-form");
const statusMessage = document.getElementById("form-status");
const submitButton = form.querySelector('button[type="submit"]');

form.addEventListener("submit", async (event) => {
  event.preventDefault();
  statusMessage.textContent = "Submitting...";
  submitButton.disabled = true;

  try {
    const response = await fetch("/api/AzFuncHttpTruck", {
      method: "POST",
      body: new FormData(form),
    });

    const result = await response.json();
    if (!response.ok) {
      throw new Error(result.message || "The form could not be submitted.");
    }

    statusMessage.textContent = result.message;
    form.reset();
  } catch (error) {
    statusMessage.textContent = error instanceof Error
      ? `Submission failed: ${error.message}`
      : "Submission failed. Please try again.";
  } finally {
    submitButton.disabled = false;
  }
});
