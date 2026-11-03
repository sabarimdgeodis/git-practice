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

    const responseText = await response.text();
    let result = {};
    if (responseText) {
      try {
        result = JSON.parse(responseText);
      } catch {
        if (response.ok) {
          throw new Error("The server returned an invalid response.");
        }
      }
    }

    if (!response.ok) {
      throw new Error(
        result.message || `The server could not submit the form (HTTP ${response.status}).`,
      );
    }

    if (typeof result.message !== "string") {
      throw new Error("The server response did not include a submission result.");
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
